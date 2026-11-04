using Fcg.Catalog.Application.Abstractions;

namespace Fcg.Catalog.Application.DTOs;

// Um resultado da busca, montado só a partir do índice: sem Ativo, porque o índice guarda apenas
// jogos ativos, e sem as datas de controle. A relevância é a pontuação do motor, sem normalização,
// e só é comparável entre itens da mesma resposta.
public record ResultadoBuscaJogoResponse(
    Guid Id,
    string Titulo,
    string? Descricao,
    decimal Preco,
    string? Desenvolvedora,
    DateTime? DataLancamento,
    double Relevancia
)
{
    public static ResultadoBuscaJogoResponse De(DocumentoJogo documento, double relevancia) =>
        new(
            documento.Id,
            documento.Titulo,
            documento.Descricao,
            documento.Preco,
            documento.Desenvolvedora,
            documento.DataLancamento,
            relevancia
        );
}
