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

// O laço inteiro, sem atalho: a escrita do jogo pela API grava a linha do outbox na mesma
// transação, o entregador leva o evento ao broker, o consumer do próprio catálogo relê o jogo e
// escreve no índice, e o endpoint de busca encontra o resultado. Nada é publicado à mão.
[Collection(BuscaCollection.Name)]
public class LacoCompletoDaBuscaTests(CatalogApiComBuscaFactory factory)
{
    private const string Rota = "/api/jogos/search";

    // O atraso que o contrato da busca aceita entre a escrita e a consulta.
    private static readonly TimeSpan s_teto = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task JogosSemeadosDevemEstarNoIndiceAoSubirSemPassoManual()
    {
        factory.IdsSemeados.Should().HaveCount(22);

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
        ).Should().BeTrue("a reconstrução automática do boot leva ao índice os jogos semeados");
    }

    [Fact]
    public async Task JogoCriadoDeveAparecerNaBuscaEmAte30Segundos()
    {
        string marcador = NovoMarcador();

        Guid id = await CriarAsync($"Lanterna {marcador}");

        (await AguardarAsync(async () => await EncontraAsync(marcador, id)))
            .Should()
            .BeTrue("o jogo criado chega ao índice pelo outbox, pelo broker e pelo consumer");
    }

    [Fact]
    public async Task TituloEditadoDeveSerEncontradoEOAntigoNaoEmAte30Segundos()
    {
        string antigo = NovoMarcador();
        string novo = NovoMarcador();
        Guid id = await CriarAsync($"Farol {antigo}");
        (await AguardarAsync(() => EncontraAsync(antigo, id)))
            .Should()
            .BeTrue("o título original deve ser indexado antes da edição");

        HttpResponseMessage edicao = await Admin()
            .PutAsJsonAsync($"/api/jogos/{id}", new { titulo = $"Farol {novo}", preco = 59.90m });
        edicao.StatusCode.Should().Be(HttpStatusCode.OK);

        (
            await AguardarAsync(async () =>
                await EncontraAsync(novo, id) && await NaoEncontraAsync(antigo, id)
            )
        )
            .Should()
            .BeTrue("o índice reflete o título novo e esquece o antigo");
    }

    [Fact]
    public async Task JogoDesativadoDeveSumirDaBuscaEmAte30Segundos()
    {
        string marcador = NovoMarcador();
        Guid id = await CriarAsync($"Bússola {marcador}");
        (await AguardarAsync(() => EncontraAsync(marcador, id)))
            .Should()
            .BeTrue("o jogo deve ser indexado antes da desativação");

        HttpResponseMessage desativacao = await Admin()
            .PatchAsync($"/api/jogos/{id}/desativar", content: null);
        desativacao.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AguardarAsync(() => NaoEncontraAsync(marcador, id)))
            .Should()
            .BeTrue("jogo desativado não pode ser comprado e sai da busca");

        // A listagem e o detalhe continuam mostrando o jogo: só a busca o esconde.
        HttpResponseMessage detalhe = await Admin().GetAsync($"/api/jogos/{id}");
        detalhe.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detalhe.Content.ReadFromJsonAsync<JogoResponse>())!.Ativo.Should().BeFalse();
    }

    // Um termo que só o jogo do teste tem: a fixture não reseta o banco, e o filtro pelo id
    // isola o teste dos jogos criados pelos outros.
    private static string NovoMarcador() => $"m{Guid.NewGuid():N}"[..13];

    private HttpClient Admin() => factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());

    private async Task<Guid> CriarAsync(string titulo)
    {
        HttpResponseMessage criacao = await Admin()
            .PostAsJsonAsync("/api/jogos", new { titulo, preco = 59.90m });
        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!.Id;
    }

    private async Task<bool> EncontraAsync(string termo, Guid id) =>
        await BuscarIdsAsync(termo) is { } ids && ids.Contains(id);

    private async Task<bool> NaoEncontraAsync(string termo, Guid id) =>
        await BuscarIdsAsync(termo) is { } ids && !ids.Contains(id);

    // Nulo quando a busca não respondeu 200: antes de a reconstrução do boot criar o índice a
    // resposta é 503, e isso não pode passar por "o jogo não está no resultado".
    private async Task<IReadOnlyList<Guid>?> BuscarIdsAsync(string termo)
    {
        HttpResponseMessage resposta = await factory
            .CreateAuthenticatedClient(JwtTestTokens.TokenUsuario(Guid.NewGuid()))
            .GetAsync($"{Rota}?q={Uri.EscapeDataString(termo)}&tamanhoPagina=100");

        if (resposta.StatusCode != HttpStatusCode.OK)
            return null;

        IReadOnlyList<ResultadoBuscaJogoResponse> resultados = (
            await resposta.Content.ReadFromJsonAsync<IReadOnlyList<ResultadoBuscaJogoResponse>>()
        )!;

        return [.. resultados.Select(r => r.Id)];
    }

    // Poll até a condição virar verdadeira (indexação é assíncrona) ou o teto estourar.
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
