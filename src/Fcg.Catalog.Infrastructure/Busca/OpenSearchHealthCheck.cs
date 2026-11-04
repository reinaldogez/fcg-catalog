using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenSearch.Client;
using EstadoDoCluster = OpenSearch.Net.HealthStatus;

namespace Fcg.Catalog.Infrastructure.Busca;

// Saúde do índice de busca: a saúde do cluster é a chamada mais barata que prova endpoint
// alcançável, assinatura aceita e primários alocados de uma vez só. Amarelo conta como saudável
// porque, num domínio de nó único, réplica nunca é alocada e o amarelo é o estado normal.
public sealed class OpenSearchHealthCheck(IOpenSearchClient cliente) : IHealthCheck
{
    // Teto próprio abaixo do timeout do cliente: sem ele, endpoint inalcançável deixaria a
    // requisição de saúde pendurada pelo timeout inteiro em vez de reportar não-saudável.
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
            ClusterHealthResponse resposta = await cliente.Cluster.HealthAsync(ct: cts.Token);

            if (!resposta.IsValid)
            {
                return HealthCheckResult.Unhealthy(
                    "Cluster de busca inacessível.",
                    resposta.OriginalException
                );
            }

            return resposta.Status is EstadoDoCluster.Green or EstadoDoCluster.Yellow
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Cluster de busca em estado {resposta.Status}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "Cluster de busca não respondeu dentro do teto de saúde."
            );
        }
        catch (Exception excecao)
        {
            return HealthCheckResult.Unhealthy("Cluster de busca inacessível.", excecao);
        }
    }
}
