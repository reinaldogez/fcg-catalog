using Fcg.Catalog.Application.DTOs;

namespace Fcg.Catalog.Application.Abstractions;

// Lado de leitura do índice de busca. Responde inteiro a partir do índice, sem tocar a fonte da
// verdade, ordenado por relevância e com desempate estável para a paginação por deslocamento.
public interface IBuscaDeJogos
{
    // Lança BuscaIndisponivelException quando a busca não pode responder: uma lista vazia nesse
    // caso afirmaria que nenhum jogo casa com o termo.
    Task<IReadOnlyList<ResultadoBuscaJogoResponse>> BuscarAsync(
        string termo,
        int deslocamento,
        int tamanho,
        CancellationToken cancellationToken = default
    );
}
