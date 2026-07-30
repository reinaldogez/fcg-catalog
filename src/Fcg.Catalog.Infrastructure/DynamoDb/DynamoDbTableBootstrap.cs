using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Infrastructure.DynamoDb;

// Definição única da forma da tabela do read model, compartilhada pelo boot da aplicação e pela
// fixture de integração. Script externo mais fixture separada dariam três definições da mesma
// tabela e ninguém as manteria sincronizadas.
public sealed class DynamoDbTableBootstrap(
    IAmazonDynamoDB cliente,
    DynamoDbSettings settings,
    ILogger<DynamoDbTableBootstrap> logger
)
{
    public const string ChaveParticao = "PK";
    public const string ChaveOrdenacao = "SK";

    private static readonly TimeSpan s_intervaloDeEspera = TimeSpan.FromMilliseconds(200);

    public async Task GarantirTabelaAsync(CancellationToken cancellationToken = default)
    {
        // Só o endpoint local é provisionado pela aplicação. Na nuvem a tabela vem de fora e uma
        // tentativa de criação exigiria permissão de escrita de esquema que o serviço não tem.
        if (!settings.UsaEndpointLocal)
        {
            return;
        }

        if (await TabelaExisteAsync(cancellationToken))
        {
            return;
        }

        await CriarTabelaAsync(cancellationToken);
        await EsperarTabelaAtivaAsync(cancellationToken);

        logger.LogInformation("Tabela {TableName} criada no endpoint local.", settings.TableName);
    }

    private async Task<bool> TabelaExisteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cliente.DescribeTableAsync(settings.TableName, cancellationToken);
            return true;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
    }

    private async Task CriarTabelaAsync(CancellationToken cancellationToken)
    {
        CreateTableRequest requisicao = new()
        {
            TableName = settings.TableName,
            // Cobrança sob demanda: a mesma forma que o provisionamento da nuvem usa, e sem
            // capacidade provisionada para dimensionar num modelo de leitura de volume irregular.
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions =
            [
                new AttributeDefinition(ChaveParticao, ScalarAttributeType.S),
                new AttributeDefinition(ChaveOrdenacao, ScalarAttributeType.S),
            ],
            KeySchema =
            [
                new KeySchemaElement(ChaveParticao, KeyType.HASH),
                new KeySchemaElement(ChaveOrdenacao, KeyType.RANGE),
            ],
        };

        try
        {
            await cliente.CreateTableAsync(requisicao, cancellationToken);
        }
        catch (ResourceInUseException)
        {
            // Outra réplica ganhou a corrida entre o describe e o create; a tabela existe, que é
            // o resultado pedido.
        }
    }

    private async Task EsperarTabelaAtivaAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            DescribeTableResponse resposta = await cliente.DescribeTableAsync(
                settings.TableName,
                cancellationToken
            );

            if (resposta.Table.TableStatus == TableStatus.ACTIVE)
            {
                return;
            }

            await Task.Delay(s_intervaloDeEspera, cancellationToken);
        }
    }
}
