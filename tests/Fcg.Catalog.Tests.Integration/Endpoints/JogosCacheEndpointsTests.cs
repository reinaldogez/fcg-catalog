using System.Net;
using System.Net.Http.Json;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Infrastructure.Cache;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

// Containers próprios (Postgres + Redis), fora da IntegrationCollection: a fixture compartilhada
// permanece sem Redis, e é ela que prova o pass-through da suíte existente de jogos.
public class JogosCacheEndpointsTests(CatalogApiComCacheFactory factory)
    : IClassFixture<CatalogApiComCacheFactory>,
        IAsyncLifetime
{
    private const string Listagem = "/api/jogos?pagina=1&tamanhoPagina=20";

    private const string PropriedadeDeCache = "cacheResultado";

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DuasListagensIdenticasDevemConsultarORepositorioUmaVez()
    {
        HttpClient client = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        await CriarJogoAsync(client, "Hollow Knight", 49.90m);

        factory.Contador.Zerar();

        HttpResponseMessage primeira = await client.GetAsync(Listagem);
        HttpResponseMessage segunda = await client.GetAsync(Listagem);

        primeira.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Jogos(primeira)).Should().BeEquivalentTo(await Jogos(segunda));

        factory.Contador.Listagens.Should().Be(1);
    }

    [Fact]
    public async Task CriacaoDeveAparecerNaListagemSeguinte()
    {
        HttpClient client = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());

        // Cacheia a listagem antes da escrita: sem o incremento de versão, a página gravada aqui
        // seria devolvida de novo e o jogo novo não apareceria.
        (await Jogos(await client.GetAsync(Listagem)))
            .Should()
            .BeEmpty();

        JogoResponse criado = await CriarJogoAsync(client, "Celeste", 39.90m);

        (await Jogos(await client.GetAsync(Listagem)))
            .Should()
            .ContainSingle(jogo => jogo.Id == criado.Id);
    }

    [Fact]
    public async Task AtualizacaoDeveRefletirNaProximaLeituraDoDetalhe()
    {
        HttpClient client = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        JogoResponse criado = await CriarJogoAsync(client, "Celeste", 39.90m);

        (await Detalhe(client, criado.Id)).Titulo.Should().Be("Celeste");

        HttpResponseMessage put = await client.PutAsJsonAsync(
            $"/api/jogos/{criado.Id}",
            new { titulo = "Celeste GOTY", preco = 29.90m }
        );
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        JogoResponse depois = await Detalhe(client, criado.Id);
        depois.Titulo.Should().Be("Celeste GOTY");
        depois.Preco.Should().Be(29.90m);
    }

    [Fact]
    public async Task DesativacaoDeveRefletirNaListagemENoDetalhe()
    {
        HttpClient client = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        JogoResponse criado = await CriarJogoAsync(client, "Limbo", 19.90m);

        (await Detalhe(client, criado.Id)).Ativo.Should().BeTrue();
        (await Jogos(await client.GetAsync(Listagem))).Should().OnlyContain(jogo => jogo.Ativo);

        HttpResponseMessage patch = await client.PatchAsync(
            $"/api/jogos/{criado.Id}/desativar",
            null
        );
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Detalhe(client, criado.Id)).Ativo.Should().BeFalse();
        (await Jogos(await client.GetAsync(Listagem)))
            .Should()
            .ContainSingle()
            .Which.Ativo.Should()
            .BeFalse();
    }

    [Fact]
    public async Task DetalheInexistenteDeveResponder404ESemGravarNoCache()
    {
        HttpClient client = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        var inexistente = Guid.NewGuid();

        (await client.GetAsync($"/api/jogos/{inexistente}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);

        ICacheCatalogo cache = factory.Services.GetRequiredService<ICacheCatalogo>();
        (await cache.ObterDetalheAsync(inexistente)).Should().BeNull();

        factory.Contador.Zerar();

        // Se a ausência tivesse sido cacheada, a segunda leitura não voltaria ao repositório.
        (await client.GetAsync($"/api/jogos/{inexistente}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
        factory.Contador.ObtencoesPorId.Should().Be(1);
    }

    [Fact]
    public async Task ComRedisInalcancavelAsLeiturasDevemResponder200ComAviso()
    {
        HttpClient client = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        JogoResponse criado = await CriarJogoAsync(client, "Gris", 45m);

        ProvedorDeLogCapturador capturador = new();

        // Mesmo Postgres, cache apontado a um endereço sem ninguém escutando: o cache falha aberto
        // e a requisição segue para a fonte da verdade.
        using WebApplicationFactory<Program> semCache = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(RedisSettings.ChaveHost, "127.0.0.1");
            builder.UseSetting(RedisSettings.ChavePort, "1");

            // Troca a fábrica de loggers inteira, e não um provedor a mais: o módulo de
            // observabilidade registra a fábrica do Serilog, e um provedor acrescentado ao
            // pipeline padrão não receberia nada. O log de requisição não passa por aqui.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILoggerFactory>();
                services.AddSingleton<ILoggerFactory>(new LoggerFactory([capturador]));
            });
        });

        HttpClient clienteSemCache = semCache.CreateClient();
        clienteSemCache.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {JwtTestTokens.TokenAdmin()}"
        );

        HttpResponseMessage lista = await clienteSemCache.GetAsync(Listagem);
        HttpResponseMessage detalhe = await clienteSemCache.GetAsync($"/api/jogos/{criado.Id}");

        lista.StatusCode.Should().Be(HttpStatusCode.OK);
        detalhe.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Jogos(lista)).Should().ContainSingle(jogo => jogo.Id == criado.Id);

        capturador.Avisos.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OLogDaRequisicaoDeLeituraDeveCarregarOResultadoDeCache()
    {
        HttpClient client = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        await CriarJogoAsync(client, "Tunic", 39.90m);

        factory.Diagnostico.Limpar();

        await client.GetAsync(Listagem);
        await client.GetAsync(Listagem);

        factory.Diagnostico.ValoresDe(PropriedadeDeCache).Should().Equal("miss", "hit");
    }

    private static async Task<JogoResponse> CriarJogoAsync(
        HttpClient client,
        string titulo,
        decimal preco
    )
    {
        HttpResponseMessage criacao = await client.PostAsJsonAsync(
            "/api/jogos",
            new { titulo, preco }
        );
        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!;
    }

    private static async Task<JogoResponse> Detalhe(HttpClient client, Guid id)
    {
        HttpResponseMessage resposta = await client.GetAsync($"/api/jogos/{id}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<JogoResponse>())!;
    }

    private static async Task<IReadOnlyList<JogoResponse>> Jogos(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<IReadOnlyList<JogoResponse>>())!;

    // Captura os avisos que o adaptador emite ao absorver a falha do cache; nenhum pacote de
    // logger falso é referenciado pelo projeto.
    private sealed class ProvedorDeLogCapturador : ILoggerProvider
    {
        public List<string> Avisos { get; } = [];

        public ILogger CreateLogger(string categoryName) => new LoggerCapturador(Avisos);

        public void Dispose() { }

        private sealed class LoggerCapturador(List<string> avisos) : ILogger
        {
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
                if (logLevel != LogLevel.Warning)
                    return;

                lock (avisos)
                {
                    avisos.Add(formatter(state, exception));
                }
            }
        }
    }
}
