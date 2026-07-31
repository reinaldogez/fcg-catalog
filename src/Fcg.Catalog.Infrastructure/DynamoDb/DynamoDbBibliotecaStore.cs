using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Fcg.Catalog.Application.Abstractions;

namespace Fcg.Catalog.Infrastructure.DynamoDb;

// Uma classe para as duas portas: a API depende só da consulta e o consumer só da escrita, mas a
// forma da chave e a conversão dos atributos são as mesmas e viveriam duplicadas em duas classes.
public sealed class DynamoDbBibliotecaStore(
    IAmazonDynamoDB cliente,
    DynamoDbSettings settings,
    int? limiteDeItensPorPagina
) : IBibliotecaReadModel, IProjecaoBiblioteca
{
    // Prefixos mantêm a tabela aberta a outros tipos de item sem migração de chave.
    private const string PrefixoUsuario = "USER#";
    private const string PrefixoJogo = "JOGO#";

    private const string AtributoUsuarioId = "usuarioId";
    private const string AtributoJogoId = "jogoId";
    private const string AtributoPedidoId = "pedidoId";
    private const string AtributoNomeJogo = "nomeJogo";
    private const string AtributoPreco = "preco";
    private const string AtributoAdquiridoEm = "adquiridoEm";

    private const string AliasChaveParticao = "#pk";
    private const string ValorChaveParticao = ":pk";

    // Formato de ida e volta: emite o sufixo Z para instante em UTC e devolve o Kind na leitura.
    private const string FormatoIso8601 = "O";

    // Composição de produção: sem teto de itens por página, e portanto com a requisição idêntica à
    // que o serviço espera. O parâmetro existe para que a continuação de página seja exercitável
    // sem depender do teto de 1 MB, que a instância local não aplica.
    public DynamoDbBibliotecaStore(IAmazonDynamoDB cliente, DynamoDbSettings settings)
        : this(cliente, settings, limiteDeItensPorPagina: null) { }

    public async Task ProjetarAsync(
        ItemBibliotecaProjetado item,
        CancellationToken cancellationToken = default
    )
    {
        // Escrita incondicional. Todo atributo é função pura do evento — nada gerado no instante
        // do processamento —, então reentrega reescreve o mesmo item byte a byte e a idempotência
        // sai de graça. Escrita condicional obrigaria a tratar exceção como fluxo normal.
        PutItemRequest requisicao = new()
        {
            TableName = settings.TableName,
            Item = ParaAtributos(item),
        };

        await cliente.PutItemAsync(requisicao, cancellationToken);
    }

    public async Task<IReadOnlyList<ItemBibliotecaProjetado>> ListarPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default
    )
    {
        QueryRequest requisicao = new()
        {
            TableName = settings.TableName,
            KeyConditionExpression = $"{AliasChaveParticao} = {ValorChaveParticao}",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                [AliasChaveParticao] = DynamoDbTableBootstrap.ChaveParticao,
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [ValorChaveParticao] = new(ChaveDeParticao(usuarioId)),
            },
            Limit = limiteDeItensPorPagina,
        };

        List<ItemBibliotecaProjetado> itens = [];

        do
        {
            QueryResponse resposta = await cliente.QueryAsync(requisicao, cancellationToken);

            itens.AddRange((resposta.Items ?? []).Select(ParaItem));

            // Continuação sinalizada não é erro: a página tem teto de 1 MB, e quem faz uma única
            // chamada trunca a biblioteca em silêncio a partir do primeiro usuário que o excede.
            requisicao.ExclusiveStartKey = resposta.LastEvaluatedKey;
        } while (requisicao.ExclusiveStartKey is { Count: > 0 });

        return itens;
    }

    private static string ChaveDeParticao(Guid usuarioId) => PrefixoUsuario + usuarioId;

    private static string ChaveDeOrdenacao(Guid jogoId) => PrefixoJogo + jogoId;

    private static Dictionary<string, AttributeValue> ParaAtributos(ItemBibliotecaProjetado item) =>
        new()
        {
            [DynamoDbTableBootstrap.ChaveParticao] = new(ChaveDeParticao(item.UsuarioId)),
            [DynamoDbTableBootstrap.ChaveOrdenacao] = new(ChaveDeOrdenacao(item.JogoId)),
            [AtributoUsuarioId] = new(item.UsuarioId.ToString()),
            [AtributoJogoId] = new(item.JogoId.ToString()),
            [AtributoPedidoId] = new(item.PedidoId.ToString()),
            [AtributoNomeJogo] = new(item.NomeJogo),
            // Conversão invariante explícita, sem passar por ponto flutuante binário: o valor pago
            // é monetário e uma ida e volta por double devolveria outro número.
            [AtributoPreco] = new AttributeValue
            {
                N = item.Preco.ToString(CultureInfo.InvariantCulture),
            },
            [AtributoAdquiridoEm] = new(ParaTextoUtc(item.AdquiridoEm)),
        };

    private static ItemBibliotecaProjetado ParaItem(Dictionary<string, AttributeValue> atributos) =>
        new(
            Guid.Parse(atributos[AtributoUsuarioId].S),
            Guid.Parse(atributos[AtributoJogoId].S),
            Guid.Parse(atributos[AtributoPedidoId].S),
            atributos[AtributoNomeJogo].S,
            decimal.Parse(atributos[AtributoPreco].N, CultureInfo.InvariantCulture),
            DateTime.Parse(
                atributos[AtributoAdquiridoEm].S,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            )
        );

    // Texto em vez de epoch numérico: legível no console, ordenável lexicograficamente e imune ao
    // Kind. Instante sem fuso é reetiquetado como UTC, e com deslocamento é convertido — assim o
    // sufixo Z que o formato emite não mente sobre o instante.
    private static string ParaTextoUtc(DateTime instante)
    {
        DateTime utc =
            instante.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(instante, DateTimeKind.Utc)
                : instante.ToUniversalTime();

        return utc.ToString(FormatoIso8601, CultureInfo.InvariantCulture);
    }
}
