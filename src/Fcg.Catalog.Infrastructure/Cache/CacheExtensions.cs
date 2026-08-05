using Fcg.Catalog.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Fcg.Catalog.Infrastructure.Cache;

public static class CacheExtensions
{
    public static IServiceCollection AddCatalogCache(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var settings = RedisSettings.Ler(configuration);

        if (!settings.Habilitado)
        {
            services.AddSingleton<ICacheCatalogo, CacheCatalogoPassThrough>();

            return services;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            // Endpoint e senha em campos tipados, não numa string de configuração: aquela é
            // separada por vírgula e por sinal de igual, e uma senha que contenha um dos dois
            // seria interpretada como outra opção do cliente.
            options.ConfigurationOptions = new ConfigurationOptions
            {
                EndPoints = { { settings.Host, settings.Port } },
                Password = settings.Password,
            };
        });

        // Sem estado próprio, e a abstração de cache distribuído já é singleton.
        services.AddSingleton<ICacheCatalogo, CacheCatalogoRedis>();

        return services;
    }
}
