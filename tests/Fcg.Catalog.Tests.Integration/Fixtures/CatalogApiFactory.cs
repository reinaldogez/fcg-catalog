using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.Services;
using Fcg.Catalog.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Sobe a API real contra um Postgres do Testcontainers. Cobre a fatia Postgres (sem RabbitMQ
// nem JWT). A mensageria não está cabeada ainda; um IPublishEndpoint no-op resolve o fluxo
// de criação de pedido sem broker.
//
// IPedidoDomainService e IPublishEndpoint só ganham registro de produção mais adiante; aqui
// são providos localmente para exercitar o endpoint de criação de pedido ponta a ponta.
public class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("catalog")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Satisfaz o fail-fast de connection string do startup e aponta o DbContext ao container.
        // O container já está de pé (StartAsync precede o build do host em InitializeAsync).
        builder.UseSetting("ConnectionStrings:Catalog", _postgres.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IPedidoDomainService, PedidoDomainService>();
            services.AddSingleton<IPublishEndpoint, PublishEndpointNoop>();
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

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
        await base.DisposeAsync();
    }
}
