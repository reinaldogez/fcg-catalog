using Amazon.DynamoDBv2;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fcg.Catalog.Infrastructure.DynamoDb;

// Prontidão do read model: descrever a tabela é a chamada mais barata que prova endpoint
// alcançável, credencial válida e tabela existente de uma vez só.
public sealed class DynamoDbHealthCheck(IAmazonDynamoDB cliente, DynamoDbSettings settings)
    : IHealthCheck
{
    // Teto próprio porque a cadeia de retry do SDK é mais longa que o intervalo de sondagem do
    // orquestrador: sem ele, endpoint inalcançável deixa a requisição de prontidão pendurada em
    // vez de reportar não-pronto.
    private static readonly TimeSpan s_teto = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(s_teto);

        try
        {
            await cliente.DescribeTableAsync(settings.TableName, cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                $"Tabela {settings.TableName} não respondeu dentro do teto de prontidão."
            );
        }
        catch (Exception excecao)
        {
            return HealthCheckResult.Unhealthy(
                $"Tabela {settings.TableName} inacessível.",
                excecao
            );
        }
    }
}
