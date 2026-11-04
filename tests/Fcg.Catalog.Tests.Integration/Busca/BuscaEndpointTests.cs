using System.Net;
using System.Net.Http.Json;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using OpenSearch.Client;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Busca;

// O endpoint de busca contra um índice de verdade: tolerância a erro de digitação sobre o catálogo
// semeado, relevância, desempate, paginação e a independência da fonte da verdade. Cada teste parte
// do índice já preenchido pela reconstrução do boot.
[Collection(BuscaCollection.Name)]
public class BuscaEndpointTests(CatalogApiComBuscaFactory factory) : IAsyncLifetime
{
    private const string Rota = "/api/jogos/search";

    private static readonly TimeSpan s_teto = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        (
            await AguardarAsync(async () =>
            {
                MultiGetResponse documentos = await factory.Inspecao.MultiGetAsync(m =>
                    m.Index(MapeamentoDoIndice.NomeDoIndice)
                        .GetMany<DocumentoJogo>(
                            factory.IdsSemeados.Select(id => id.ToString()),
                            (operacao, _) => operacao.Index(MapeamentoDoIndice.NomeDoIndice)
                        )
                );

                return documentos.IsValid && documentos.Hits.All(h => h.Found);
            })
        ).Should().BeTrue("os jogos semeados devem estar no índice antes de qualquer busca");

        // A leitura por id enxerga o documento antes de a busca enxergar: o refresh fecha a
        // diferença sem depender do intervalo do índice.
        await factory.Inspecao.Indices.RefreshAsync(MapeamentoDoIndice.NomeDoIndice);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("witcer", new[] { "The Witcher 3: Wild Hunt" })]
    [InlineData("bloodborn", new[] { "Bloodborne" })]
    [InlineData(
        "fromsoftwre",
        new[]
        {
            "Bloodborne",
            "Sekiro: Shadows Die Twice",
            "Elden Ring",
            "Elden Ring — Edição Colecionador",
        }
    )]
    [InlineData("pos apocaliptico", new[] { "Fallout 4", "Horizon Zero Dawn" })]
    public async Task BuscaComErroDeDigitacaoDeveEncontrarOsJogosSemeados(
        string termo,
        string[] esperados
    )
    {
        IReadOnlyList<ResultadoBuscaJogoResponse> resultado = await BuscarAsync(termo);

        resultado.Select(r => r.Titulo).Should().Contain(esperados);
    }

    // As duas edições têm "elden ring" no título; a mais curta casa com menos ruído e vem antes.
    [Fact]
    public async Task EldenRingDeveVirAntesDaEdicaoColecionador()
    {
        List<string> titulos = [.. (await BuscarAsync("elden rnig")).Select(r => r.Titulo)];

        titulos.Should().Contain(["Elden Ring", "Elden Ring — Edição Colecionador"]);
        titulos
            .IndexOf("Elden Ring")
            .Should()
            .BeLessThan(titulos.IndexOf("Elden Ring — Edição Colecionador"));
    }

    // O termo exato pontua acima da variante a uma edição de distância: a reescrita do fuzzy
    // pondera cada termo expandido pela semelhança com o termo buscado.
    [Fact]
    public async Task TermoExatoDevePontuarAcimaDaVarianteComUmaEdicao()
    {
        string exato = NovoMarcador();
        string variante = string.Concat(exato.AsSpan(0, exato.Length - 1), "z");
        Guid idExato = await CriarAsync(exato);
        Guid idVariante = await CriarAsync(variante);

        List<ResultadoBuscaJogoResponse> doTeste = [];
        (
            await AguardarAsync(async () =>
            {
                doTeste =
                [
                    .. (await BuscarSePossivelAsync(exato) ?? []).Where(r =>
                        r.Id == idExato || r.Id == idVariante
                    ),
                ];
                return doTeste.Count == 2;
            })
        ).Should().BeTrue("os dois jogos devem estar no índice");

        doTeste[0].Id.Should().Be(idExato);
        doTeste[0].Relevancia.Should().BeGreaterThan(doTeste[1].Relevancia);
    }

    [Fact]
    public async Task RelevanciaDeveSerNaoCrescenteAoLongoDaResposta()
    {
        IReadOnlyList<ResultadoBuscaJogoResponse> resultado = await BuscarAsync("rpg mundo aberto");

        resultado.Should().HaveCountGreaterThan(1);
        resultado.Select(r => r.Relevancia).Should().BeInDescendingOrder();
    }

    // Títulos idênticos empatam na pontuação, e o id decide: sem desempate, a mesma página
    // poderia trazer itens diferentes a cada consulta.
    [Fact]
    public async Task EmpateDeRelevanciaDeveSerDesempatadoPeloIdEPaginarDeFormaEstavel()
    {
        string marcador = NovoMarcador();
        Guid[] ids =
        [
            await CriarAsync($"Empate {marcador}"),
            await CriarAsync($"Empate {marcador}"),
            await CriarAsync($"Empate {marcador}"),
        ];
        string[] ordemEsperada = [.. ids.Select(id => id.ToString()).Order(StringComparer.Ordinal)];

        List<ResultadoBuscaJogoResponse> empatados = [];
        (
            await AguardarAsync(async () =>
            {
                empatados = [.. await BuscarSePossivelAsync(marcador) ?? []];
                return empatados.Count == 3;
            })
        ).Should().BeTrue("os três jogos devem estar no índice");

        empatados.Select(r => r.Relevancia).Distinct().Should().ContainSingle();
        empatados.Select(r => r.Id.ToString()).Should().Equal(ordemEsperada);

        // Página a página, um item por vez: a mesma ordem, e depois do último, lista vazia.
        List<string> paginados = [];
        for (int pagina = 1; pagina <= 3; pagina++)
        {
            IReadOnlyList<ResultadoBuscaJogoResponse> unico = await BuscarAsync(
                marcador,
                pagina,
                tamanhoPagina: 1
            );
            paginados.Add(unico.Single().Id.ToString());
        }

        paginados.Should().Equal(ordemEsperada);
        (await BuscarAsync(marcador, pagina: 4, tamanhoPagina: 1)).Should().BeEmpty();
    }

    [Fact]
    public async Task PaginaAlemDoResultadoDeveResponder200ComListaVazia() =>
        (await BuscarAsync("witcer", pagina: 500, tamanhoPagina: 20)).Should().BeEmpty();

    // A resposta sai só do índice: nenhuma leitura do repositório durante a requisição.
    [Fact]
    public async Task BuscaNaoDeveConsultarAFonteDaVerdade()
    {
        factory.Contador.Zerar();

        IReadOnlyList<ResultadoBuscaJogoResponse> resultado = await BuscarAsync("witcer");

        resultado.Should().NotBeEmpty();
        factory.Contador.Listagens.Should().Be(0);
        factory.Contador.ObtencoesPorId.Should().Be(0);
    }

    // Um termo que só o jogo do teste tem: a fixture não reseta o banco.
    private static string NovoMarcador() => $"m{Guid.NewGuid():N}"[..13];

    private async Task<Guid> CriarAsync(string titulo)
    {
        HttpResponseMessage criacao = await factory
            .CreateAuthenticatedClient(JwtTestTokens.TokenAdmin())
            .PostAsJsonAsync("/api/jogos", new { titulo, preco = 59.90m });
        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!.Id;
    }

    private async Task<IReadOnlyList<ResultadoBuscaJogoResponse>> BuscarAsync(
        string termo,
        int pagina = 1,
        int tamanhoPagina = 100
    )
    {
        HttpResponseMessage resposta = await Requisitar(termo, pagina, tamanhoPagina);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        return (
            await resposta.Content.ReadFromJsonAsync<IReadOnlyList<ResultadoBuscaJogoResponse>>()
        )!;
    }

    // Nulo quando a busca não respondeu 200, para o polling seguir tentando.
    private async Task<IReadOnlyList<ResultadoBuscaJogoResponse>?> BuscarSePossivelAsync(
        string termo
    )
    {
        HttpResponseMessage resposta = await Requisitar(termo, 1, 100);

        return resposta.StatusCode == HttpStatusCode.OK
            ? await resposta.Content.ReadFromJsonAsync<IReadOnlyList<ResultadoBuscaJogoResponse>>()
            : null;
    }

    // Usuário comum: a busca exige só autenticação.
    private Task<HttpResponseMessage> Requisitar(string termo, int pagina, int tamanhoPagina) =>
        factory
            .CreateAuthenticatedClient(JwtTestTokens.TokenUsuario(Guid.NewGuid()))
            .GetAsync(
                $"{Rota}?q={Uri.EscapeDataString(termo)}&pagina={pagina}&tamanhoPagina={tamanhoPagina}"
            );

    private static async Task<bool> AguardarAsync(Func<Task<bool>> condicao)
    {
        DateTime limite = DateTime.UtcNow + s_teto;
        while (DateTime.UtcNow < limite)
        {
            if (await condicao())
                return true;
            await Task.Delay(250);
        }
        return false;
    }
}
