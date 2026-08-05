using System.Globalization;
using System.Net.Http.Headers;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Infrastructure.Cache;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Serilog;
using Serilog.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Sobe a API real contra Postgres + Redis do Testcontainers, para exercitar o cache-aside pelo
// endpoint. É fixture separada porque a compartilhada permanece sem Redis — é sobre ela que o
// pass-through em nível de endpoint é provado, e editá-la apagaria justamente essa prova.
//
// Broker e read model não participam de nenhum teste daqui: o bus nunca é iniciado (sem os hosted
// services) e o DynamoDB fica na forma de nuvem, em que o bootstrap do boot é inerte.
public class CatalogApiComCacheFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string NomeDaTabelaDeLeitura = "biblioteca";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("catalog")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();

    // Exposto para o host derivado que aponta o cache a um endereço morto.
    public string RedisHost { get; private set; } = string.Empty;

    public int RedisPort { get; private set; }

    public ContadorDeConsultasDeJogo Contador { get; } = new();

    public RegistroDeDiagnostico Diagnostico { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Catalog", _postgres.GetConnectionString());

        // O bus é composto mas nunca iniciado; os campos existem só para satisfazer o fail-fast
        // do host do broker.
        builder.UseSetting("RabbitMq:Host", "localhost");
        builder.UseSetting("RabbitMq:Username", "guest");
        builder.UseSetting("RabbitMq:Password", "guest");

        // Sem ServiceUrl: a forma de nuvem, em que o bootstrap da tabela devolve sem tocar a rede.
        builder.UseSetting("DynamoDb:TableName", NomeDaTabelaDeLeitura);

        builder.UseSetting(RedisSettings.ChaveHost, RedisHost);
        builder.UseSetting(
            RedisSettings.ChavePort,
            RedisPort.ToString(CultureInfo.InvariantCulture)
        );

        builder.UseSetting("Jwt:JwksUri", $"{JwtTestTokens.TestIssuer}/.well-known/jwks.json");
        builder.UseSetting("Jwt:Issuer", JwtTestTokens.TestIssuer);
        builder.UseSetting("Jwt:Audience", JwtTestTokens.TestAudience);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            // O cliente do read model não é usado por nenhum teste daqui; a substituição existe
            // só para que a cadeia de credenciais do SDK não seja consultada numa máquina sem
            // perfil da AWS configurado.
            services.RemoveAll<IAmazonDynamoDB>();
            services.AddSingleton<IAmazonDynamoDB>(
                new AmazonDynamoDBClient(
                    new BasicAWSCredentials("local", "local"),
                    new AmazonDynamoDBConfig
                    {
                        ServiceURL = "http://127.0.0.1:1",
                        AuthenticationRegion = "us-east-1",
                    }
                )
            );

            // Envolve o repositório real; o cache-aside continua vendo o mesmo comportamento.
            services.RemoveAll<IJogoRepository>();
            services.AddScoped<IJogoRepository>(sp => new JogoRepositoryContado(
                ActivatorUtilities.CreateInstance<JogoRepository>(sp),
                Contador
            ));

            services.RemoveAll<IDiagnosticContext>();
            services.AddSingleton<IDiagnosticContext>(sp => new DiagnosticContextEspiao(
                sp.GetRequiredService<DiagnosticContext>(),
                Diagnostico
            ));

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
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        string[] endereco = _redis.GetConnectionString().Split(':', 2);
        RedisHost = endereco[0];
        RedisPort = int.Parse(endereco[1], CultureInfo.InvariantCulture);

        using IServiceScope scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    }

    // Reset entre testes: a tabela volta vazia, o contador zera e o cache perde inclusive a chave
    // de versão — ela não vence, e a versão herdada de um teste deslocaria as chaves do seguinte.
    public async Task ResetAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Database.ExecuteSqlRawAsync(
                "TRUNCATE jogos, pedidos, itens_biblioteca RESTART IDENTITY CASCADE;"
            );

        await _redis.ExecAsync(["redis-cli", "FLUSHALL"]);

        Contador.Zerar();
        Diagnostico.Limpar();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await base.DisposeAsync();
    }
}
