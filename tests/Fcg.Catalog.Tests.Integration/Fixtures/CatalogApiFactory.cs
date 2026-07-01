using System.Net.Http.Headers;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.Services;
using Fcg.Catalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Sobe a API real contra Postgres + RabbitMQ do Testcontainers. O bus é cabeado de verdade
// (Outbox provê o IPublishEndpoint escrevendo na OutboxMessage via DbContext); os hosted
// services do MassTransit são removidos no host de teste — o sweeper do Outbox dá deadlock com
// o reset de banco entre testes, e IBus/IPublishEndpoint seguem resolvíveis sem ele.
//
// IPedidoDomainService só ganha registro de produção mais adiante; aqui é provido localmente
// para exercitar o endpoint de criação de pedido ponta a ponta.
public class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("catalog")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder(
        "rabbitmq:3.13-management-alpine"
    ).Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Satisfaz o fail-fast de connection string do startup e aponta o DbContext ao container.
        // O container já está de pé (StartAsync precede o build do host em InitializeAsync).
        builder.UseSetting("ConnectionStrings:Catalog", _postgres.GetConnectionString());

        // Host do bus por campos separados, lidos tarde do container (porta dinâmica). Deriva
        // os quatro campos da connection string amqp do Testcontainer.
        Uri amqp = new(_rabbitMq.GetConnectionString());
        string[] credenciais = amqp.UserInfo.Split(':', 2);
        builder.UseSetting("RabbitMq:Host", amqp.Host);
        builder.UseSetting("RabbitMq:Port", amqp.Port.ToString());
        builder.UseSetting("RabbitMq:Username", Uri.UnescapeDataString(credenciais[0]));
        builder.UseSetting("RabbitMq:Password", Uri.UnescapeDataString(credenciais[1]));

        // Satisfaz o fail-fast do JwtSettings; issuer/audience casam com os tokens da fixture.
        builder.UseSetting("Jwt:JwksUri", $"{JwtTestTokens.TestIssuer}/.well-known/jwks.json");
        builder.UseSetting("Jwt:Issuer", JwtTestTokens.TestIssuer);
        builder.UseSetting("Jwt:Audience", JwtTestTokens.TestAudience);

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IPedidoDomainService, PedidoDomainService>();

            // Sem os hosted services do MassTransit, o bus não sobe sozinho (evita o deadlock do
            // sweeper do Outbox com o reset de banco). O bus é iniciado sob demanda nos testes de
            // topologia; publish continua indo para a OutboxMessage via DbContext.
            services.RemoveAll<IHostedService>();

            // Injeta a chave pública de teste como configuração estática do handler: valida os
            // tokens da fixture sem buscar o JWKS real na rede.
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

    // Cliente já autenticado com o Bearer informado — açúcar para os testes de auth.
    public HttpClient CreateAuthenticatedClient(string token)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        using IServiceScope scope = Services.CreateScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.MigrateAsync();
    }

    // Reset entre testes (coleção em série): as tabelas voltam vazias.
    public async Task ResetAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE jogos, pedidos, itens_biblioteca RESTART IDENTITY CASCADE;"
        );
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await base.DisposeAsync();
    }
}
