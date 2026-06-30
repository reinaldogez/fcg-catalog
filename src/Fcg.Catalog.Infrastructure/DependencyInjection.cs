using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fcg.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        string? connectionString = configuration.GetConnectionString("Catalog");

        services.AddDbContext<CatalogDbContext>(options =>
        {
            // Boot normal sem connection string não conecta ao banco (migração é ato explícito).
            if (string.IsNullOrWhiteSpace(connectionString))
                options.UseNpgsql();
            else
                options.UseNpgsql(connectionString);

            options.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IJogoRepository, JogoRepository>();
        services.AddScoped<IPedidoRepository, PedidoRepository>();
        services.AddScoped<IItemBibliotecaRepository, ItemBibliotecaRepository>();

        // Mesmo CatalogDbContext scoped resolve o UnitOfWork — repos e UoW compartilham contexto.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        return services;
    }
}
