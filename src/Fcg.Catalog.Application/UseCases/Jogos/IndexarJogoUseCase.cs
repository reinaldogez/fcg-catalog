using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Application.UseCases.Jogos;

// Leva ao índice de busca o estado atual de um jogo. Relê a fonte da verdade em vez de confiar no
// evento: o evento só diz qual jogo mudou, e o estado que vale é o que está gravado agora. Não
// comita nem abre transação — o consumer só lê o banco relacional, e a escrita no índice é a
// operação inteira.
public class IndexarJogoUseCase(
    IJogoRepository jogoRepository,
    IIndiceDeJogos indiceDeJogos,
    ILogger<IndexarJogoUseCase> logger
)
{
    public async Task ExecutarAsync(Guid jogoId, CancellationToken cancellationToken = default)
    {
        Jogo? jogo = await jogoRepository.ObterPorIdAsync(jogoId, cancellationToken);

        // Jogo que não existe mais na fonte não tem instante de alteração para versionar a
        // remoção: remove sem condição, e documento ausente no índice já é o efeito desejado.
        ResultadoEscritaIndice resultado = jogo is null
            ? await indiceDeJogos.RemoverAsync(jogoId, null, cancellationToken)
            : await EscreverAsync(indiceDeJogos, jogo, cancellationToken);

        switch (resultado)
        {
            case ResultadoEscritaIndice.IndiceAusente:
                // Não é falha: quem cria o índice é a reconstrução, que só lê a fonte depois de
                // criá-lo, e por isso traz este jogo junto. Repetir a mensagem não mudaria nada.
                logger.LogInformation(
                    "Índice de busca ausente; jogo {JogoId} fica para a reconstrução.",
                    jogoId
                );
                break;

            case ResultadoEscritaIndice.Obsoleta:
                logger.LogDebug(
                    "Índice de busca já tem versão mais nova do jogo {JogoId}; escrita descartada.",
                    jogoId
                );
                break;
        }
    }

    // A decisão única jogo → escrita, compartilhada com a reconstrução: ativo é gravado, inativo é
    // removido, e as duas operações levam a versão relida do banco. Dois caminhos com decisões
    // próprias divergiriam na primeira regra nova.
    internal static Task<ResultadoEscritaIndice> EscreverAsync(
        IIndiceDeJogos indiceDeJogos,
        Jogo jogo,
        CancellationToken cancellationToken
    ) =>
        jogo.Ativo
            ? indiceDeJogos.GravarAsync(
                DocumentoJogo.De(jogo),
                VersaoDoJogo.De(jogo),
                cancellationToken
            )
            : indiceDeJogos.RemoverAsync(jogo.Id, VersaoDoJogo.De(jogo), cancellationToken);
}
