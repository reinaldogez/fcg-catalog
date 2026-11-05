namespace Fcg.Catalog.Application.Abstractions;

// Lado de escrita do índice de busca. As duas operações são as mesmas para quem indexa a partir
// do evento e para quem reconstrói a partir da fonte da verdade, e ambas são versionadas: a
// escrita de um estado mais velho que o gravado é descartada, seja qual for a ordem de chegada.
public interface IIndiceDeJogos
{
    Task<ResultadoEscritaIndice> GravarAsync(
        DocumentoJogo documento,
        long versao,
        CancellationToken cancellationToken = default
    );

    // Versão nula remove sem condição: é o caso do jogo que não existe mais na fonte, onde não há
    // instante de alteração a comparar.
    Task<ResultadoEscritaIndice> RemoverAsync(
        Guid id,
        long? versao,
        CancellationToken cancellationToken = default
    );
}
