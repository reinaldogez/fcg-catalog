using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Cache;

// Standalone (sem containers): o config-gate é decidido na composição, então a forma desligada é
// asserível sem Redis algum. Fica fora da IntegrationCollection de propósito.
public class CacheCatalogoConfigGateTests
{
    [Fact]
    public async Task SemHostAComposicaoDeveSubirComAdaptadorPassThrough()
    {
        await using ServiceProvider provider = Compor([]);

        ICacheCatalogo cache = provider.GetRequiredService<ICacheCatalogo>();

        cache.Should().BeOfType<CacheCatalogoPassThrough>();

        // Pass-through, e não cache em memória: o que foi gravado não volta na leitura seguinte.
        await cache.GravarListagemAsync(1, 20, [Jogo()]);
        await cache.GravarDetalheAsync(Jogo());

        (await cache.ObterListagemAsync(1, 20)).Should().BeNull();
        (await cache.ObterDetalheAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ComHostAComposicaoDeveRegistrarOAdaptadorDeRedis()
    {
        // Endereço qualquer: o cliente é preguiçoso e nada conecta na composição.
        await using ServiceProvider provider = Compor(
            new Dictionary<string, string?>
            {
                [RedisSettings.ChaveHost] = "redis.local",
                [RedisSettings.ChavePort] = "6380",
            }
        );

        provider.GetRequiredService<ICacheCatalogo>().Should().BeOfType<CacheCatalogoRedis>();
    }

    // A instrumentação de Redis registra o profiler na conexão que resolve do container, e só
    // instrumenta chamadas feitas por ela. Conexão registrada é o que separa span emitido de
    // span ausente, e a ausência não quebra teste algum — por isso a composição é asserida aqui.
    // Asserido no descritor, e não resolvendo do provider: resolver conectaria de fato, e o
    // endereço aqui não existe.
    [Fact]
    public void ComHostAConexaoDeveSerRegistradaComoSingleton()
    {
        ServiceDescriptor? conexao = Descrever(
            new Dictionary<string, string?>
            {
                [RedisSettings.ChaveHost] = "redis.local",
                [RedisSettings.ChavePort] = "6380",
            }
        );

        conexao.Should().NotBeNull("a instrumentação resolve a conexão do container");
        conexao!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void SemHostNaoDeveRegistrarConexao() => Descrever([]).Should().BeNull();

    [Fact]
    public void PortaIlegivelDeveSerRecusada()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [RedisSettings.ChaveHost] = "redis.local",
                    [RedisSettings.ChavePort] = "seis mil",
                }
            )
            .Build();

        Action ler = () => RedisSettings.Ler(configuration);

        ler.Should().Throw<InvalidOperationException>().WithMessage("*Redis:Port*");
    }

    private static ServiceDescriptor? Descrever(Dictionary<string, string?> entradas)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCatalogCache(
            new ConfigurationBuilder().AddInMemoryCollection(entradas).Build()
        );

        return services.FirstOrDefault(descritor =>
            descritor.ServiceType == typeof(IConnectionMultiplexer)
        );
    }

    private static ServiceProvider Compor(Dictionary<string, string?> entradas)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCatalogCache(
            new ConfigurationBuilder().AddInMemoryCollection(entradas).Build()
        );

        return services.BuildServiceProvider();
    }

    private static JogoResponse Jogo() =>
        new(
            Guid.NewGuid(),
            "Jogo sem cache",
            null,
            19.90m,
            null,
            null,
            true,
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)
        );
}
