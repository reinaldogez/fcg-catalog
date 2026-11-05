using Fcg.Catalog.Application.Abstractions;
using OpenSearch.Client;

namespace Fcg.Catalog.Infrastructure.Busca;

// O serviço é dono da forma do índice: nome, settings e mapping vivem aqui, e só a preparação do
// índice os aplica.
public static class MapeamentoDoIndice
{
    // Constante, não configuração: o domínio de busca é dedicado ao catálogo.
    public const string NomeDoIndice = "jogos";

    // Bloqueia a criação automática só deste índice e mantém a de qualquer outro: um bloqueio
    // global barraria também os índices internos dos plugins do domínio gerenciado. Sem ele, uma
    // remoção com versão externa criaria o índice com mapeamento dinâmico, e a reconstrução veria
    // um índice existente com a forma errada.
    public static readonly IReadOnlyDictionary<string, string> ConfiguracaoDoCluster =
        new Dictionary<string, string> { ["action.auto_create_index"] = "-jogos,+*" };

    // Minúsculas e dobra de acentos antes do fuzzy: "pos apocaliptico" casa "pós-apocalíptico"
    // sem gastar o orçamento de edições com o acento. Sem stemmer: os títulos são em sua maioria
    // em inglês, e radical com fuzzy fica imprevisível.
    private const string NomeDoAnalisador = "texto_do_catalogo";

    public static ICreateIndexRequest Configurar(CreateIndexDescriptor indice) =>
        indice
            .Settings(settings =>
                settings
                    // Réplica nunca é alocada num domínio de nó único, e com ela o índice
                    // ficaria amarelo para sempre.
                    .NumberOfShards(1)
                    .NumberOfReplicas(0)
                    .Analysis(analise =>
                        analise.Analyzers(analisadores =>
                            analisadores.Custom(
                                NomeDoAnalisador,
                                analisador =>
                                    analisador
                                        .Tokenizer("standard")
                                        .Filters("lowercase", "asciifolding")
                            )
                        )
                    )
            )
            .Map<DocumentoJogo>(mapeamento =>
                mapeamento
                    // Campo fora do mapping é recusado: protege a forma única do documento.
                    .Dynamic(DynamicMapping.Strict)
                    .Properties(campos =>
                        campos
                            // Keyword porque é o desempate da ordenação por relevância.
                            .Keyword(campo => campo.Name(documento => documento.Id))
                            .Text(campo =>
                                campo.Name(documento => documento.Titulo).Analyzer(NomeDoAnalisador)
                            )
                            .Text(campo =>
                                campo
                                    .Name(documento => documento.Descricao)
                                    .Analyzer(NomeDoAnalisador)
                            )
                            .Text(campo =>
                                campo
                                    .Name(documento => documento.Desenvolvedora)
                                    .Analyzer(NomeDoAnalisador)
                            )
                            // Só devolvidos no resultado, nunca consultados. Preço em centavos
                            // inteiros: o valor monetário não passa por ponto flutuante.
                            .Number(campo =>
                                campo
                                    .Name(documento => documento.Preco)
                                    .Type(NumberType.ScaledFloat)
                                    .ScalingFactor(100)
                                    .Index(false)
                            )
                            .Date(campo =>
                                campo.Name(documento => documento.DataLancamento).Index(false)
                            )
                    )
            );
}
