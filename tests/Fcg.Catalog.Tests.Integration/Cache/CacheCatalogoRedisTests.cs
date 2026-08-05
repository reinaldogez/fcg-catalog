using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Infrastructure.Cache;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Cache;

// Container próprio, fora da IntegrationCollection: o adaptador não precisa de Postgres, RabbitMQ
// nem DynamoDB, e a fixture compartilhada segue sem Redis.
//
// O grafo vem da própria extensão de composição, então o caminho com host configurado — cliente
// apontado ao container e adaptador de Redis registrado — é exercitado por todo teste daqui.
public class CacheCatalogoRedisTests : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private readonly ServiceProvider _provider;
    private readonly ICacheCatalogo _cache;

    public CacheCatalogoRedisTests(RedisFixture redis)
    {
        _redis = redis;

        Dictionary<string, string?> entradas = new()
        {
            [RedisSettings.ChaveHost] = redis.Host,
            [RedisSettings.ChavePort] = redis.Port.ToString(),
        };

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCatalogCache(
            new ConfigurationBuilder().AddInMemoryCollection(entradas).Build()
        );

        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheCatalogo>();
    }

    public Task InitializeAsync() => _redis.LimparAsync();

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task PaginasDistintasDevemProduzirChavesDistintasSemContaminacao()
    {
        await _cache.GravarListagemAsync(1, 20, [Jogo("Primeira página")]);
        await _cache.GravarListagemAsync(2, 20, [Jogo("Segunda página")]);

        IReadOnlyList<JogoResponse>? primeira = await _cache.ObterListagemAsync(1, 20);
        IReadOnlyList<JogoResponse>? segunda = await _cache.ObterListagemAsync(2, 20);

        primeira.Should().ContainSingle().Which.Titulo.Should().Be("Primeira página");
        segunda.Should().ContainSingle().Which.Titulo.Should().Be("Segunda página");

        // O tamanho de página também compõe a chave: a mesma página com outro tamanho é outro
        // conteúdo, e devolvê-la seria resposta errada, não velha.
        (await _cache.ObterListagemAsync(1, 50))
            .Should()
            .BeNull();

        // Literais que o roteiro de demo inspeciona no Redis.
        (await _redis.Inspecao.KeyExistsAsync("jogos:lista:v0:p1:t20"))
            .Should()
            .BeTrue();
        (await _redis.Inspecao.KeyExistsAsync("jogos:lista:v0:p2:t20")).Should().BeTrue();
    }

    [Fact]
    public async Task IncrementoDeVersaoDeveTornarInalcancaveisAsChavesDaVersaoAnterior()
    {
        await _cache.GravarListagemAsync(1, 20, [Jogo("Antes da invalidação")]);

        await _cache.InvalidarListagemAsync();

        (await _cache.ObterListagemAsync(1, 20)).Should().BeNull();

        // Inalcançável, e não apagada: a versão nova muda o prefixo e as órfãs morrem pelo prazo.
        (await _redis.Inspecao.KeyExistsAsync("jogos:lista:v0:p1:t20"))
            .Should()
            .BeTrue();

        // A abstração de cache distribuído grava cada entrada como hash, com o conteúdo no campo
        // "data" e os prazos nos outros dois — inspecionar a chave como string devolveria erro de
        // tipo, e é por isso que a leitura por fora do adaptador passa pelo campo.
        ((string?)await _redis.Inspecao.HashGetAsync("jogos:versao", "data"))
            .Should()
            .Be("1");

        // A versão não vence: se ela sumisse, o contador voltaria ao início e as páginas antigas
        // ainda vivas voltariam a ser alcançáveis com conteúdo obsoleto.
        (await _redis.Inspecao.KeyTimeToLiveAsync("jogos:versao"))
            .Should()
            .BeNull();

        await _cache.GravarListagemAsync(1, 20, [Jogo("Depois da invalidação")]);

        (await _redis.Inspecao.KeyExistsAsync("jogos:lista:v1:p1:t20")).Should().BeTrue();

        IReadOnlyList<JogoResponse>? depois = await _cache.ObterListagemAsync(1, 20);

        depois.Should().ContainSingle().Which.Titulo.Should().Be("Depois da invalidação");
    }

    [Fact]
    public async Task GravacaoDeveAplicarPrazoDeCincoMinutosNaListagemENoDetalhe()
    {
        JogoResponse jogo = Jogo("Com prazo");

        await _cache.GravarListagemAsync(1, 20, [jogo]);
        await _cache.GravarDetalheAsync(jogo);

        TimeSpan? prazoDaListagem = await _redis.Inspecao.KeyTimeToLiveAsync(
            "jogos:lista:v0:p1:t20"
        );
        TimeSpan? prazoDoDetalhe = await _redis.Inspecao.KeyTimeToLiveAsync($"jogos:{jogo.Id}");

        prazoDaListagem
            .Should()
            .BeCloseTo(TimeSpan.FromMinutes(5), precision: TimeSpan.FromSeconds(10));
        prazoDoDetalhe
            .Should()
            .BeCloseTo(TimeSpan.FromMinutes(5), precision: TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task DetalheGravadoDeveVoltarIntegroEDesaparecerNaInvalidacao()
    {
        JogoResponse jogo = Jogo("Detalhe do catálogo");

        await _cache.GravarDetalheAsync(jogo);

        (await _cache.ObterDetalheAsync(jogo.Id)).Should().BeEquivalentTo(jogo);

        await _cache.InvalidarDetalheAsync(jogo.Id);

        (await _cache.ObterDetalheAsync(jogo.Id)).Should().BeNull();
    }

    private static JogoResponse Jogo(string titulo) =>
        new(
            Guid.NewGuid(),
            titulo,
            "Descrição do jogo",
            19.90m,
            "Estúdio",
            new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            true,
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc)
        );
}
