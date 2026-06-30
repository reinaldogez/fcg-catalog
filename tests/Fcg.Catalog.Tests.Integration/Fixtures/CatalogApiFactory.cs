using Fcg.Catalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Sobe um host mínimo (só persistência) contra um Postgres real do Testcontainers.
// Cobre só a fatia Postgres (sem RabbitMQ nem JWT).
public class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("catalog")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Religa o DbContext ao container. ConfigureTestServices roda após o AddInfrastructure,
        // então sobrescreve a connection string (que o registro de produção lê cedo demais).
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<CatalogDbContext>));

            services.AddDbContext<CatalogDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention()
            );
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
