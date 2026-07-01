using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

public class JogosEndpointsTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ObterJogoInexistenteDeveRetornar404()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());

        HttpResponseMessage resposta = await client.GetAsync($"/api/jogos/{Guid.NewGuid()}");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CriarJogoComTituloInvalidoDeveRetornar400ProblemJsonComTraceId()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        var request = new { titulo = new string('a', 201), preco = 10m };

        HttpResponseMessage resposta = await client.PostAsJsonAsync("/api/jogos", request);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
        corpo.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        corpo.GetProperty("status").GetInt32().Should().Be(400);
        corpo.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CriarObterEListarJogo_CaminhoFeliz()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        var novo = new
        {
            titulo = "Hollow Knight",
            preco = 49.90m,
            descricao = "Metroidvania",
        };

        HttpResponseMessage criacao = await client.PostAsJsonAsync("/api/jogos", novo);
        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        JogoResponse? criado = await criacao.Content.ReadFromJsonAsync<JogoResponse>();
        criado.Should().NotBeNull();
        criado!.Titulo.Should().Be("Hollow Knight");

        HttpResponseMessage leitura = await client.GetAsync($"/api/jogos/{criado.Id}");
        leitura.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage lista = await client.GetAsync("/api/jogos");
        lista.StatusCode.Should().Be(HttpStatusCode.OK);
        IReadOnlyList<JogoResponse>? jogos = await lista.Content.ReadFromJsonAsync<
            IReadOnlyList<JogoResponse>
        >();
        jogos.Should().ContainSingle(jogo => jogo.Id == criado.Id);
    }

    [Fact]
    public async Task AtualizarEDesativarJogo()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        var novo = new { titulo = "Celeste", preco = 39.90m };
        HttpResponseMessage criacao = await client.PostAsJsonAsync("/api/jogos", novo);
        JogoResponse criado = (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!;

        var atualizacao = new { titulo = "Celeste GOTY", preco = 29.90m };
        HttpResponseMessage put = await client.PutAsJsonAsync(
            $"/api/jogos/{criado.Id}",
            atualizacao
        );
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage patch = await client.PatchAsync(
            $"/api/jogos/{criado.Id}/desativar",
            null
        );
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage putInexistente = await client.PutAsJsonAsync(
            $"/api/jogos/{Guid.NewGuid()}",
            atualizacao
        );
        putInexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
