using System.Net.Http.Headers;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Infrastructure.DynamoDb;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Infrastructure.Persistence.Repositories;
using Fcg.Catalog.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.DynamoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Sobe a API real com a busca ligada, contra Postgres + RabbitMQ + DynamoDB local + OpenSearch do
// Testcontainers, para provar o laço inteiro: escrita do jogo, linha no outbox, entrega ao broker,
// consumer de indexação e índice consultado pelo endpoint.
//
// Ao contrário da fixture compartilhada, os serviços de fundo ficam ligados — o bus, o entregador
// do outbox e a reconstrução automática do índice — e não há reset de banco entre testes: foi o
// reset que travou com o varredor do outbox. Cada teste cria os próprios jogos e filtra pelos ids
// deles. A compartilhada continua sem OpenSearch, porque é ela que prova a busca desligada.
public class CatalogApiComBuscaFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string NomeDaTabelaDeLeitura = "biblioteca";

    // As mesmas imagens da fixture compartilhada.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("catalog")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder(
        "rabbitmq:3.13-management-alpine"
    ).Build();

    private readonly DynamoDbContainer _dynamoDb = new DynamoDbBuilder(
        "amazon/dynamodb-local:2.5.2"
    ).Build();

    private readonly OpenSearchFixture _openSearch = new();

    // Cliente direto do cluster, para inspecionar o índice por fora das portas da aplicação.
    public OpenSearch.Client.IOpenSearchClient Inspecao => _openSearch.Inspecao;

    // Os jogos que o seed gravou antes de o host subir: é o que a reconstrução automática tem de
    // levar ao índice sem passo manual.
    public IReadOnlyList<Guid> IdsSemeados { get; private set; } = [];

    public ContadorDeConsultasDeJogo Contador { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Catalog", _postgres.GetConnectionString());

        Uri amqp = new(_rabbitMq.GetConnectionString());
        string[] credenciais = amqp.UserInfo.Split(':', 2);
        builder.UseSetting("RabbitMq:Host", amqp.Host);
        builder.UseSetting("RabbitMq:Port", amqp.Port.ToString());
        builder.UseSetting("RabbitMq:Username", Uri.UnescapeDataString(credenciais[0]));
        builder.UseSetting("RabbitMq:Password", Uri.UnescapeDataString(credenciais[1]));

        builder.UseSetting("DynamoDb:TableName", NomeDaTabelaDeLeitura);
        builder.UseSetting("DynamoDb:ServiceUrl", _dynamoDb.GetConnectionString());

        // Sem assinatura: o container roda sem o plugin de segurança e fala http.
        builder.UseSetting(OpenSearchSettings.ChaveEndpoint, _openSearch.Endpoint);
        builder.UseSetting(OpenSearchSettings.ChaveUseSigV4, "false");

        builder.UseSetting("Jwt:JwksUri", $"{JwtTestTokens.TestIssuer}/.well-known/jwks.json");
        builder.UseSetting("Jwt:Issuer", JwtTestTokens.TestIssuer);
        builder.UseSetting("Jwt:Audience", JwtTestTokens.TestAudience);

        builder.ConfigureTestServices(services =>
        {
            // Conta só as consultas feitas dentro de uma requisição HTTP: o consumer de indexação
            // e a reconstrução também leem o repositório, em segundo plano, e somariam ao contador
            // o que não é custo do endpoint.
            services.AddHttpContextAccessor();
            services.RemoveAll<IJogoRepository>();
            services.AddScoped<IJogoRepository>(sp =>
            {
                JogoRepository real = ActivatorUtilities.CreateInstance<JogoRepository>(sp);
                bool emRequisicao =
                    sp.GetRequiredService<IHttpContextAccessor>().HttpContext is not null;

                return emRequisicao ? new JogoRepositoryContado(real, Contador) : real;
            });

            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    OpenIdConnectConfiguration configuracao = new();
                    configuracao.SigningKeys.Add(JwtTestTokens.PublicSecurityKey);
                    options.Configuration = configuracao;
                    options.TokenValidationParameters.IssuerSigningKey =
                        JwtTestTokens.PublicSecurityKey;
                }
            );
        });
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitMq.StartAsync(),
            _dynamoDb.StartAsync(),
            _openSearch.InitializeAsync()
        );

        await MigrarESemearAsync();

        // A primeira resolução constrói o host e liga os serviços de fundo; a reconstrução
        // automática encontra o banco já semeado, como no primeiro boot de uma sessão.
        using IServiceScope scope = Services.CreateScope();

        // Aqui a falha tem de estourar: o boot absorve a ausência da tabela com aviso, e um
        // container fora do ar viraria testes vermelhos por outra razão.
        await scope
            .ServiceProvider.GetRequiredService<DynamoDbTableBootstrap>()
            .GarantirTabelaAsync();
    }

    // Antes do host, direto no banco do container: os jogos semeados não geram evento, e só a
    // reconstrução automática do boot os leva ao índice.
    private async Task MigrarESemearAsync()
    {
        DbContextOptions<CatalogDbContext> opcoes = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using CatalogDbContext contexto = new(opcoes);
        await contexto.Database.MigrateAsync();
        await new CatalogSeeder(contexto, NullLogger<CatalogSeeder>.Instance).SeedAsync();

        IdsSemeados = await contexto.Jogos.Select(jogo => jogo.Id).ToListAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // O host primeiro: o bus para com o broker ainda de pé.
        await base.DisposeAsync();
        await _openSearch.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _dynamoDb.DisposeAsync();
    }
}
