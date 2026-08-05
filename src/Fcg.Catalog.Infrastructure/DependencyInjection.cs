using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.Services;
using Fcg.Catalog.Infrastructure.Cache;
using Fcg.Catalog.Infrastructure.DynamoDb;
using Fcg.Catalog.Infrastructure.Messaging;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Infrastructure.Persistence.Repositories;
using Fcg.Catalog.Infrastructure.Seed;
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

        // Fonte da reconstrução do modelo de leitura: junção sobre o mesmo contexto dos repos.
        services.AddScoped<IFonteReprojecaoBiblioteca, EfFonteReprojecaoBiblioteca>();

        // Invariantes de criação de pedido que consultam repositório — resolve aqui porque suas
        // dependências (os três repos acima) vivem nesta extensão; infra-de-domínio, mesmo padrão.
        services.AddScoped<IPedidoDomainService, PedidoDomainService>();

        // Mesmo CatalogDbContext scoped resolve o UnitOfWork — repos e UoW compartilham contexto.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        // Seeder do catálogo inicial — resolvido pelo Job (--seed); no boot normal fica ocioso.
        services.AddScoped<CatalogSeeder>();

        // Armazenamento do read model da biblioteca — extensão própria, no mesmo padrão da
        // mensageria: falha na composição se o nome da tabela não estiver configurado.
        services.AddCatalogReadModelStore(configuration);

        // Cache do catálogo — config-gated: sem host de Redis a extensão registra o pass-through
        // e a aplicação sobe sem cache, em vez de falhar por dependência opcional ausente.
        services.AddCatalogCache(configuration);

        services.AddCatalogMessaging(configuration);

        return services;
    }
}
