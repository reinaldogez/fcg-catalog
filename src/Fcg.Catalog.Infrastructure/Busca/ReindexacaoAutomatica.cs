using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.UseCases.Jogos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Infrastructure.Busca;

// Prepara e preenche o índice de busca quando o serviço sobe, sem segurar o boot: o domínio de
// busca pode nascer depois do serviço, e até lá só a busca fica indisponível. Roda uma vez por
// processo e termina; queda do cluster no meio da execução é caso da reconstrução manual.
public sealed class ReindexacaoAutomatica(
    IPreparacaoDoIndice preparacaoDoIndice,
    IServiceScopeFactory scopeFactory,
    ILogger<ReindexacaoAutomatica> logger
) : BackgroundService
{
    // O mesmo valor do timeout do cliente: uma sondagem não se sobrepõe à anterior.
    private static readonly TimeSpan s_intervaloDaSondagem = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await AguardarClusterAsync(stoppingToken);

            // Escopo próprio: o use case lê a fonte pelo repositório, que vive no escopo do banco.
            await using AsyncServiceScope escopo = scopeFactory.CreateAsyncScope();
            await escopo
                .ServiceProvider.GetRequiredService<ReindexarJogosUseCase>()
                .ExecutarAsync(somenteSeVazio: true, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligamento do processo antes de o cluster ficar pronto: nada a registrar.
        }
        catch (Exception excecao)
        {
            // Não derruba o processo: o resto do serviço não depende da busca.
            logger.LogError(
                excecao,
                "Reconstrução automática do índice de busca falhou; o remédio é a reconstrução manual."
            );
        }
    }

    // Uma linha de aviso na primeira falha e uma de informação quando o cluster responde: o
    // domínio pode levar minutos para nascer, e uma linha por tentativa só poluiria o log.
    private async Task AguardarClusterAsync(CancellationToken stoppingToken)
    {
        bool avisou = false;

        while (!await preparacaoDoIndice.EstaSaudavelAsync(stoppingToken))
        {
            if (!avisou)
            {
                logger.LogWarning(
                    "Cluster de busca indisponível; nova tentativa a cada {Intervalo}.",
                    s_intervaloDaSondagem
                );
                avisou = true;
            }
            else
            {
                logger.LogDebug("Cluster de busca ainda indisponível.");
            }

            await Task.Delay(s_intervaloDaSondagem, stoppingToken);
        }

        logger.LogInformation("Cluster de busca disponível; preparando o índice.");
    }
}
