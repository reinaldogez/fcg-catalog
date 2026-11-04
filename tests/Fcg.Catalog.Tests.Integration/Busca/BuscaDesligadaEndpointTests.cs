using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Busca;

// O contrato HTTP da busca que não depende de um índice de verdade: autenticação, validação do
// termo e da janela, e a falha fechada. A fixture compartilhada não tem OpenSearch, então é ela que
// prova a busca desligada; o cluster fora do ar vem de um host derivado apontado a uma porta
// fechada.
public class BuscaDesligadaEndpointTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Rota = "/api/jogos/search";

    // O texto publicado no documento OpenAPI, repetido aqui de propósito: o teste prova o texto,
    // e não só a presença de alguma descrição.
    private const string DescricaoEsperada =
        "Busca jogos do catálogo no índice de busca — uma projeção em OpenSearch alimentada pelos eventos de criação, edição e desativação de jogo. A busca tolera erros de digitação e ordena os resultados por relevância; o campo `relevancia` só é comparável entre itens da mesma resposta. O índice reflete o catálogo com um pequeno atraso: um jogo recém-criado ou editado pode ainda não constar, ou constar com o estado anterior. Jogos desativados não aparecem. Para o estado atual de um jogo específico, consulte `GET /api/jogos/{id}`, que lê a fonte da verdade.";

    [Fact]
    public async Task BuscaSemTokenDeveResponder401()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"{Rota}?q=witcher");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=")]
    [InlineData("?q=%20%20%20")]
    public async Task TermoAusenteOuEmBrancoDeveResponder400ProblemDetails(string consulta)
    {
        HttpResponseMessage resposta = await ClienteDeUsuario().GetAsync($"{Rota}{consulta}");

        await DeveSerProblemaAsync(resposta, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TermoCom201CaracteresDepoisDoTrimDeveResponder400()
    {
        string termo = Uri.EscapeDataString($"  {new string('a', 201)}  ");

        HttpResponseMessage resposta = await ClienteDeUsuario().GetAsync($"{Rota}?q={termo}");

        await DeveSerProblemaAsync(resposta, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task JanelaAcimaDe10000DeveResponder400()
    {
        HttpResponseMessage resposta = await ClienteDeUsuario()
            .GetAsync($"{Rota}?q=witcher&pagina=101&tamanhoPagina=100");

        await DeveSerProblemaAsync(resposta, HttpStatusCode.BadRequest);
    }

    // Busca desligada falha fechada: uma lista vazia afirmaria que nenhum jogo casa com o termo.
    [Fact]
    public async Task ComBuscaDesligadaTermoValidoDeveResponder503EnaoListaVazia()
    {
        HttpResponseMessage resposta = await ClienteDeUsuario().GetAsync($"{Rota}?q=witcher");

        JsonElement corpo = await DeveSerProblemaAsync(resposta, HttpStatusCode.ServiceUnavailable);
        corpo.GetProperty("title").GetString().Should().Be("Busca indisponível");
    }

    // O cluster fora do ar desde o boot derruba só a busca: a prontidão segue de pé.
    [Fact]
    public async Task ComClusterInalcancavelABuscaDeveResponder503EAProntidao200()
    {
        using WebApplicationFactory<Program> comBuscaForaDoAr = Factory.WithWebHostBuilder(
            builder => builder.UseSetting(OpenSearchSettings.ChaveEndpoint, "http://127.0.0.1:1")
        );

        HttpClient anonimo = comBuscaForaDoAr.CreateClient();
        HttpClient autenticado = comBuscaForaDoAr.CreateClient();
        autenticado.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            JwtTestTokens.TokenUsuario(Guid.NewGuid())
        );

        (await anonimo.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        await DeveSerProblemaAsync(
            await autenticado.GetAsync($"{Rota}?q=witcher"),
            HttpStatusCode.ServiceUnavailable
        );
    }

    // O segmento literal não captura o detalhe, e o detalhe não captura o segmento literal.
    [Fact]
    public async Task DetalheDeveContinuarRespondendoPorGuid()
    {
        HttpClient admin = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        HttpResponseMessage criacao = await admin.PostAsJsonAsync(
            "/api/jogos",
            new { titulo = "Hollow Knight", preco = 49.90m }
        );
        JogoResponse criado = (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!;

        HttpResponseMessage detalhe = await ClienteDeUsuario().GetAsync($"/api/jogos/{criado.Id}");

        detalhe.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detalhe.Content.ReadFromJsonAsync<JogoResponse>())!.Id.Should().Be(criado.Id);
    }

    // O host de teste roda em ambiente de desenvolvimento, onde o documento OpenAPI é publicado.
    [Fact]
    public async Task DocumentoOpenApiDeveTrazerADescricaoDaBusca()
    {
        HttpResponseMessage resposta = await Factory.CreateClient().GetAsync("/openapi/v1.json");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        JsonElement operacao = documento
            .RootElement.GetProperty("paths")
            .GetProperty(Rota)
            .GetProperty("get");

        operacao.GetProperty("description").GetString().Should().Be(DescricaoEsperada);
        operacao
            .GetProperty("responses")
            .EnumerateObject()
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo("200", "400", "503");
    }

    // Usuário comum, sem papel de administrador: a busca é leitura e exige só autenticação.
    private HttpClient ClienteDeUsuario() =>
        Factory.CreateAuthenticatedClient(JwtTestTokens.TokenUsuario(Guid.NewGuid()));

    private static async Task<JsonElement> DeveSerProblemaAsync(
        HttpResponseMessage resposta,
        HttpStatusCode status
    )
    {
        resposta.StatusCode.Should().Be(status);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        JsonElement corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("status").GetInt32().Should().Be((int)status);
        corpo.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        return corpo;
    }
}
