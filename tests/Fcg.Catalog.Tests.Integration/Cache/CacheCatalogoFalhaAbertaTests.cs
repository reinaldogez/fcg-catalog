using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Cache;

// Standalone (sem containers): a dependência precisa estar inalcançável, e um container de pé
// seria justamente o que o teste não quer. Fica fora da IntegrationCollection de propósito.
public class CacheCatalogoFalhaAbertaTests
{
    [Fact]
    public async Task ComRedisInalcancavelLeituraDeveSerMissEEscritaDeveApenasAvisar()
    {
        LoggerCapturador<CacheCatalogoRedis> logger = new();

        using RedisCache cacheMorto = CacheParaEnderecoMorto();
        CacheCatalogoRedis cache = new(cacheMorto, logger);

        (await cache.ObterListagemAsync(1, 20)).Should().BeNull();
        (await cache.ObterDetalheAsync(Guid.NewGuid())).Should().BeNull();

        Func<Task> escrever = async () =>
        {
            await cache.GravarListagemAsync(1, 20, [Jogo()]);
            await cache.GravarDetalheAsync(Jogo());
            await cache.InvalidarListagemAsync();
            await cache.InvalidarDetalheAsync(Guid.NewGuid());
        };

        await escrever.Should().NotThrowAsync();

        logger.Avisos.Should().NotBeEmpty();
    }

    // Endereço sem ninguém escutando, com a política de reconexão encurtada: a dependência segue
    // inalcançável e só a espera do cliente diminui, para o teste não pagar o tempo de conexão
    // padrão em cada uma das seis operações.
    private static RedisCache CacheParaEnderecoMorto() =>
        new(
            Options.Create(
                new RedisCacheOptions
                {
                    ConfigurationOptions = new ConfigurationOptions
                    {
                        EndPoints = { { "127.0.0.1", 1 } },
                        AbortOnConnectFail = true,
                        ConnectRetry = 0,
                        ConnectTimeout = 500,
                    },
                }
            )
        );

    private static JogoResponse Jogo() =>
        new(
            Guid.NewGuid(),
            "Jogo sem cache alcançável",
            null,
            19.90m,
            null,
            null,
            true,
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)
        );

    private sealed class LoggerCapturador<T> : ILogger<T>
    {
        public List<string> Avisos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Avisos.Add(formatter(state, exception));
        }
    }
}
