using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using OpenSearch.Client;
using OpenSearch.Net;

namespace Fcg.Catalog.Infrastructure.Busca;

// Uma classe para as portas do índice: escrita, preparação e consulta compartilham o nome, a
// forma do documento e a leitura das respostas do cluster, que viveriam duplicadas em classes
// separadas.
//
// O cliente não lança em resposta de erro: devolve a resposta inválida. Na escrita, os desfechos
// esperados são traduzidos aqui e qualquer outro vira exceção, para que o consumer repita; na
// consulta, toda falha é indisponibilidade da busca.
public sealed class OpenSearchIndiceDeJogos(IOpenSearchClient cliente)
    : IIndiceDeJogos,
        IPreparacaoDoIndice,
        IBuscaDeJogos
{
    private const string ConflitoDeVersao = "version_conflict_engine_exception";
    private const string IndiceInexistente = "index_not_found_exception";
    private const string IndiceJaExistente = "resource_already_exists_exception";

    // O título pesa mais que o resto: casar no nome do jogo diz mais do que casar na descrição.
    private const double PesoDoTitulo = 3;

    // Os padrões do motor, declarados para que a semântica do fuzzy não mude com a versão do
    // cluster: sem prefixo exato, até 50 termos por expansão, transposição contando uma edição.
    private const int TamanhoDoPrefixoExato = 0;
    private const int ExpansoesMaximas = 50;

    public async Task<IReadOnlyList<ResultadoBuscaJogoResponse>> BuscarAsync(
        string termo,
        int deslocamento,
        int tamanho,
        CancellationToken cancellationToken = default
    )
    {
        ISearchResponse<DocumentoJogo> resposta;

        try
        {
            resposta = await cliente.SearchAsync<DocumentoJogo>(
                busca =>
                    busca
                        .Index(MapeamentoDoIndice.NomeDoIndice)
                        .From(deslocamento)
                        .Size(tamanho)
                        // Melhor campo: aceita fuzzy, e a pontuação de um documento é a do campo
                        // que melhor casa. Com "ou", palavra a mais ou com erro não zera o
                        // resultado, e quem casa mais termos sobe pela relevância.
                        .Query(consulta =>
                            consulta.MultiMatch(multi =>
                                multi
                                    .Type(TextQueryType.BestFields)
                                    .Fields(campos =>
                                        campos
                                            .Field(documento => documento.Titulo, PesoDoTitulo)
                                            .Field(documento => documento.Desenvolvedora)
                                            .Field(documento => documento.Descricao)
                                    )
                                    .Query(termo)
                                    .Fuzziness(Fuzziness.Auto)
                                    .Operator(Operator.Or)
                                    .PrefixLength(TamanhoDoPrefixoExato)
                                    .MaxExpansions(ExpansoesMaximas)
                                    .FuzzyTranspositions()
                            )
                        )
                        // Desempate pelo id: sem ele, a paginação por deslocamento sobre empates
                        // de pontuação não é determinística.
                        .Sort(ordem =>
                            ordem
                                .Descending(SortSpecialField.Score)
                                .Ascending(documento => documento.Id)
                        )
                        .TrackScores(),
                cancellationToken
            );
        }
        catch (Exception excecao) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BuscaIndisponivelException("O índice de busca não respondeu.", excecao);
        }

        if (!resposta.IsValid)
        {
            // Cliente que desistiu não é indisponibilidade: o cancelamento segue o seu caminho.
            cancellationToken.ThrowIfCancellationRequested();

            throw new BuscaIndisponivelException(
                "O índice de busca não respondeu.",
                Falha(resposta, "buscar")
            );
        }

        return
        [
            .. resposta.Hits.Select(acerto =>
                ResultadoBuscaJogoResponse.De(acerto.Source, acerto.Score ?? 0)
            ),
        ];
    }

    public async Task<ResultadoEscritaIndice> GravarAsync(
        DocumentoJogo documento,
        long versao,
        CancellationToken cancellationToken = default
    )
    {
        // Maior ou igual, e não só maior: reescrever o mesmo estado é o caso normal de reentrega e
        // de reconstrução, e não pode virar conflito. Sem refresh forçado: a visibilidade fica no
        // intervalo de refresh do índice, que é o atraso que o contrato da busca já declara.
        IndexResponse resposta = await cliente.IndexAsync(
            documento,
            indexacao =>
                indexacao
                    .Index(MapeamentoDoIndice.NomeDoIndice)
                    .Id(documento.Id)
                    .Version(versao)
                    .VersionType(VersionType.ExternalGte),
            cancellationToken
        );

        return Classificar(resposta, $"gravar o jogo {documento.Id}");
    }

    public async Task<ResultadoEscritaIndice> RemoverAsync(
        Guid id,
        long? versao,
        CancellationToken cancellationToken = default
    )
    {
        DeleteRequest requisicao = new(MapeamentoDoIndice.NomeDoIndice, id);

        if (versao is long valor)
        {
            requisicao.Version = valor;
            requisicao.VersionType = VersionType.ExternalGte;
        }

        DeleteResponse resposta = await cliente.DeleteAsync(requisicao, cancellationToken);

        // Documento que já não estava no índice: o efeito desejado já vale.
        if (resposta.ServerError is null && resposta.Result == Result.NotFound)
        {
            return ResultadoEscritaIndice.Aplicada;
        }

        return Classificar(resposta, $"remover o jogo {id}");
    }

    public async Task<bool> EstaSaudavelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ClusterHealthResponse resposta = await cliente.Cluster.HealthAsync(
                ct: cancellationToken
            );

            // Amarelo conta: num domínio de nó único é o estado normal, com os primários alocados.
            return resposta.IsValid && resposta.Status is HealthStatus.Green or HealthStatus.Yellow;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task GarantirAsync(CancellationToken cancellationToken = default)
    {
        // Antes da criação: uma escrita que chegue entre os dois passos encontra o índice ausente
        // e é descartada, em vez de criá-lo com mapeamento dinâmico.
        ClusterPutSettingsResponse bloqueio = await cliente.Cluster.PutSettingsAsync(
            settings =>
                settings.Persistent(persistentes =>
                {
                    foreach (
                        (string chave, string valor) in MapeamentoDoIndice.ConfiguracaoDoCluster
                    )
                    {
                        persistentes.Add(chave, valor);
                    }

                    return persistentes;
                }),
            cancellationToken
        );

        if (!bloqueio.IsValid)
        {
            throw Falha(bloqueio, "bloquear a criação automática do índice");
        }

        CreateIndexResponse criacao = await cliente.Indices.CreateAsync(
            MapeamentoDoIndice.NomeDoIndice,
            MapeamentoDoIndice.Configurar,
            cancellationToken
        );

        // Já existente é sucesso: dois pods sobem juntos num rollout e só um cria.
        if (!criacao.IsValid && TipoDoErro(criacao) != IndiceJaExistente)
        {
            throw Falha(criacao, "criar o índice");
        }
    }

    public async Task<long> ContarAsync(CancellationToken cancellationToken = default)
    {
        CountResponse resposta = await cliente.CountAsync<DocumentoJogo>(
            contagem => contagem.Index(MapeamentoDoIndice.NomeDoIndice),
            cancellationToken
        );

        return resposta.IsValid ? resposta.Count : throw Falha(resposta, "contar o índice");
    }

    private static ResultadoEscritaIndice Classificar(ResponseBase resposta, string operacao)
    {
        if (resposta.IsValid)
        {
            return ResultadoEscritaIndice.Aplicada;
        }

        return TipoDoErro(resposta) switch
        {
            ConflitoDeVersao => ResultadoEscritaIndice.Obsoleta,
            IndiceInexistente => ResultadoEscritaIndice.IndiceAusente,
            _ => throw Falha(resposta, operacao),
        };
    }

    private static string? TipoDoErro(ResponseBase resposta) => resposta.ServerError?.Error?.Type;

    // Sem o corpo da requisição na mensagem: o motivo do cluster ou a falha de transporte bastam
    // para o diagnóstico, e o log não carrega o documento.
    private static InvalidOperationException Falha(IResponse resposta, string operacao) =>
        new(
            $"Falha ao {operacao} no índice de busca: "
                + (
                    resposta.ServerError?.Error?.Reason
                    ?? resposta.OriginalException?.Message
                    ?? $"HTTP {resposta.ApiCall?.HttpStatusCode}"
                ),
            resposta.OriginalException
        );
}
