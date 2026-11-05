using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Application.UseCases.Jogos;

// Reconstrói o índice de busca a partir da fonte da verdade. Escreve pela mesma decisão e pela
// mesma operação versionada do consumer de indexação, então correr junto dele, ou em dois pods ao
// mesmo tempo, não deixa estado velho no índice. Não apaga o índice — destruí-lo é operação externa.
public class ReindexarJogosUseCase(
    IPreparacaoDoIndice preparacaoDoIndice,
    IIndiceDeJogos indiceDeJogos,
    IJogoRepository jogoRepository,
    ILogger<ReindexarJogosUseCase> logger
)
{
    // Mesmo tamanho da reconstrução da biblioteca: a ida ao banco não domina e a página
    // materializada não pesa. A escrita é documento a documento, sem falha parcial de lote a tratar.
    private const int TamanhoDaPagina = 500;

    // Com somenteSeVazio, um índice que já tem documentos fica como está: é a reconstrução de
    // quando o serviço sobe, que só deve preencher um índice recém-criado. Sem a condição, tudo é
    // reescrito, o que é inócuo para o estado que o índice já tem.
    public async Task<ResumoReindexacao> ExecutarAsync(
        bool somenteSeVazio,
        CancellationToken cancellationToken = default
    )
    {
        // O índice existe antes de a fonte ser lida: o que foi gravado antes daqui entra pela
        // leitura abaixo, e o que vier depois entra pelo consumer, que já encontra o índice criado.
        await preparacaoDoIndice.GarantirAsync(cancellationToken);

        if (somenteSeVazio && await preparacaoDoIndice.ContarAsync(cancellationToken) > 0)
        {
            logger.LogInformation("Índice de busca já preenchido; reconstrução pulada.");
            return new ResumoReindexacao(0, 0, Pulou: true);
        }

        int indexados = 0;
        int removidos = 0;

        for (int pagina = 1; ; pagina++)
        {
            IReadOnlyList<Jogo> jogos = await jogoRepository.ListarAsync(
                pagina,
                TamanhoDaPagina,
                cancellationToken
            );

            if (jogos.Count == 0)
                break;

            foreach (Jogo jogo in jogos)
            {
                ResultadoEscritaIndice resultado = await IndexarJogoUseCase.EscreverAsync(
                    indiceDeJogos,
                    jogo,
                    cancellationToken
                );

                // O índice acabou de ser garantido; se sumiu no meio, o resumo não pode se
                // apresentar como reconstrução completa.
                if (resultado == ResultadoEscritaIndice.IndiceAusente)
                {
                    throw new InvalidOperationException(
                        "O índice de busca deixou de existir durante a reconstrução."
                    );
                }

                if (jogo.Ativo)
                    indexados++;
                else
                    removidos++;
            }
        }

        logger.LogInformation(
            "Reconstrução do índice de busca concluída — {Indexados} indexados, {Removidos} removidos.",
            indexados,
            removidos
        );

        return new ResumoReindexacao(indexados, removidos, Pulou: false);
    }
}
