using Fcg.Catalog.Application.Abstractions;
using Microsoft.Extensions.Caching.StackExchangeRedis;
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

        // Endpoint e senha em campos tipados, não numa string de configuração: aquela é
        // separada por vírgula e por sinal de igual, e uma senha que contenha um dos dois
        // seria interpretada como outra opção do cliente.
        ConfigurationOptions opcoesDeConexao = new()
        {
            EndPoints = { { settings.Host, settings.Port } },
            Password = settings.Password,
        };

        // A conexão é registrada no container, e não deixada implícita dentro do cache, porque
        // quem observa o Redis precisa da mesma instância para registrar o profiler nela: um
        // multiplexer privado do cache não é alcançável de fora e não renderia span algum.
        //
        // Lazy de propósito, preservando o comportamento de conectar na primeira operação: este
        // cache falha aberto, e conectar na composição transformaria Redis fora do ar em falha
        // de startup.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(opcoesDeConexao)
        );

        // A fábrica resolve do container, para que o profiler da instrumentação e o cache
        // observem um único multiplexer em vez de abrirem conexões distintas.
        //
        // O cache distribuído fecha essa conexão ao ser descartado, sem verificar se ela é dele.
        // Isso só acontece na parada do processo, quando a conexão seria encerrada de qualquer
        // forma, e por isso é aceito em vez de contornado.
        services
            .AddOptions<RedisCacheOptions>()
            .Configure<IServiceProvider>(
                (options, provider) =>
                    options.ConnectionMultiplexerFactory = () =>
                        Task.FromResult(provider.GetRequiredService<IConnectionMultiplexer>())
            );

        services.AddStackExchangeRedisCache(_ => { });

        // Sem estado próprio, e a abstração de cache distribuído já é singleton.
        services.AddSingleton<ICacheCatalogo, CacheCatalogoRedis>();

        return services;
    }
}
