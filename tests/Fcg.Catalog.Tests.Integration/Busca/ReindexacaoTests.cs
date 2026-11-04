using System.Text.Json;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSearch.Client;
using OpenSearch.Net;
using Xunit;
using MetodoHttp = OpenSearch.Net.HttpMethod;

namespace Fcg.Catalog.Tests.Integration.Busca;

// A reconstrução manual contra o índice que a reconstrução do boot já preencheu: reescrever tudo é
// inócuo para o estado gravado, e o índice nunca é apagado e recriado no caminho.
[Collection(BuscaCollection.Name)]
public class ReindexacaoTests(CatalogApiComBuscaFactory factory)
{
    private const string Indice = MapeamentoDoIndice.NomeDoIndice;

    private static readonly TimeSpan s_teto = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ReconstruirDuasVezesDeveManterOEstadoSemApagarOIndice()
    {
        (await AguardarSemeadosAsync())
            .Should()
            .BeTrue("a reconstrução do boot deve ter preenchido o índice");

        string indiceAntes = await IdentificadorDoIndiceAsync();

        ResumoReindexacao primeiro = await ReindexarAsync();
        IReadOnlyDictionary<Guid, (DocumentoJogo, long)> depoisDoPrimeiro = await IndexadosAsync();

        ResumoReindexacao segundo = await ReindexarAsync();
        IReadOnlyDictionary<Guid, (DocumentoJogo, long)> depoisDoSegundo = await IndexadosAsync();

        // O mesmo índice físico: apagar e recriar trocaria o identificador.
        (await IdentificadorDoIndiceAsync())
            .Should()
            .Be(indiceAntes);

        primeiro.Pulou.Should().BeFalse("a reconstrução manual não tem a condição de vazio");
        segundo.Should().Be(primeiro);
        depoisDoSegundo.Should().BeEquivalentTo(depoisDoPrimeiro);

        // E o estado é o da fonte da verdade: ativos com a versão relida, inativos ausentes.
        IReadOnlyList<Jogo> jogos = await JogosDoBancoAsync();
        primeiro.Indexados.Should().Be(jogos.Count(j => j.Ativo));
        primeiro.Removidos.Should().Be(jogos.Count(j => !j.Ativo));
        depoisDoSegundo
            .Should()
            .BeEquivalentTo(
                jogos
                    .Where(j => j.Ativo)
                    .ToDictionary(j => j.Id, j => (DocumentoJogo.De(j), VersaoDoJogo.De(j)))
            );
    }

    private async Task<ResumoReindexacao> ReindexarAsync()
    {
        await using AsyncServiceScope escopo = factory.Services.CreateAsyncScope();
        return await escopo
            .ServiceProvider.GetRequiredService<ReindexarJogosUseCase>()
            .ExecutarAsync(somenteSeVazio: false);
    }

    private async Task<IReadOnlyList<Jogo>> JogosDoBancoAsync()
    {
        await using AsyncServiceScope escopo = factory.Services.CreateAsyncScope();
        return await escopo
            .ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Jogos.AsNoTracking()
            .ToListAsync();
    }

    // Todos os documentos com a versão gravada, depois de um refresh para a busca enxergar as
    // escritas recentes.
    private async Task<IReadOnlyDictionary<Guid, (DocumentoJogo, long)>> IndexadosAsync()
    {
        await factory.Inspecao.Indices.RefreshAsync(Indice);

        ISearchResponse<DocumentoJogo> resposta = await factory.Inspecao.SearchAsync<DocumentoJogo>(
            s => s.Index(Indice).Size(10_000).Version().Query(q => q.MatchAll())
        );

        resposta.IsValid.Should().BeTrue(resposta.DebugInformation);

        return resposta.Hits.ToDictionary(h => h.Source.Id, h => (h.Source, h.Version));
    }

    private async Task<string> IdentificadorDoIndiceAsync()
    {
        StringResponse resposta = await factory.Inspecao.LowLevel.DoRequestAsync<StringResponse>(
            MetodoHttp.GET,
            $"{Indice}/_settings",
            CancellationToken.None
        );

        resposta.Success.Should().BeTrue(resposta.Body);

        using var json = JsonDocument.Parse(resposta.Body);
        return json
            .RootElement.GetProperty(Indice)
            .GetProperty("settings")
            .GetProperty("index")
            .GetProperty("uuid")
            .GetString()!;
    }

    private async Task<bool> AguardarSemeadosAsync()
    {
        DateTime limite = DateTime.UtcNow + s_teto;
        while (DateTime.UtcNow < limite)
        {
            MultiGetResponse documentos = await factory.Inspecao.MultiGetAsync(m =>
                m.Index(Indice)
                    .GetMany<DocumentoJogo>(
                        factory.IdsSemeados.Select(id => id.ToString()),
                        (operacao, _) => operacao.Index(Indice)
                    )
            );

            if (documentos.IsValid && documentos.Hits.All(h => h.Found))
                return true;

            await Task.Delay(250);
        }
        return false;
    }
}
