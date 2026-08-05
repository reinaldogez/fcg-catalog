using Fcg.Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Application.UseCases.Biblioteca;

// Reconstrói o modelo de leitura inteiro a partir da fonte da verdade. Escreve pela mesma porta do
// caminho de evento: uma única mecânica de escrita no sistema é o que faz os dois caminhos
// convergirem sem prova adicional. Não apaga nada — destruir o modelo de leitura é operação externa.
public class ReprojetarBibliotecaUseCase(
    IFonteReprojecaoBiblioteca fonte,
    IProjecaoBiblioteca projecaoBiblioteca,
    ILogger<ReprojetarBibliotecaUseCase> logger
)
{
    // Grande o bastante para que a ida ao banco não domine, pequena o bastante para que a página
    // materializada não pese. A escrita é item a item de qualquer forma: agrupar não reduz o custo
    // do armazenamento sob cobrança por item, e traria falha parcial a tratar.
    private const int TamanhoDaPagina = 500;

    public async Task<ResumoReprojecaoBiblioteca> ExecutarAsync(
        CancellationToken cancellationToken = default
    )
    {
        int projetados = 0;
        int pulados = 0;
        int deslocamento = 0;

        while (true)
        {
            IReadOnlyList<LinhaReprojecaoBiblioteca> pagina = await fonte.LerPaginaAsync(
                deslocamento,
                TamanhoDaPagina,
                cancellationToken
            );

            if (pagina.Count == 0)
                break;

            foreach (LinhaReprojecaoBiblioteca linha in pagina)
            {
                if (
                    linha.PedidoId is not { } pedidoId
                    || linha.Preco is not { } preco
                    || linha.NomeJogo is not { } nomeJogo
                )
                {
                    // Linha sem contraparte não aborta a reconstrução: é dado a investigar, não
                    // razão para deixar o resto do modelo de leitura desatualizado.
                    logger.LogWarning(
                        "Item de biblioteca sem pedido aprovado ou jogo correspondente — usuário {UsuarioId}, jogo {JogoId}.",
                        linha.UsuarioId,
                        linha.JogoId
                    );

                    pulados++;
                    continue;
                }

                await projecaoBiblioteca.ProjetarAsync(
                    new ItemBibliotecaProjetado(
                        linha.UsuarioId,
                        linha.JogoId,
                        pedidoId,
                        nomeJogo,
                        preco,
                        linha.AdquiridoEm
                    ),
                    cancellationToken
                );

                projetados++;
            }

            // Avança pelo total lido, não pelo total escrito: o deslocamento é sobre a fonte, e a
            // linha pulada ocupa posição nela.
            deslocamento += pagina.Count;
        }

        ResumoReprojecaoBiblioteca resumo = new(projetados, pulados);

        logger.LogInformation(
            "Reconstrução da biblioteca concluída — {Projetados} projetados, {Pulados} pulados.",
            resumo.Projetados,
            resumo.Pulados
        );

        return resumo;
    }
}
