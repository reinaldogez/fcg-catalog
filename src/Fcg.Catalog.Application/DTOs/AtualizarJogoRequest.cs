namespace Fcg.Catalog.Application.DTOs;

public record AtualizarJogoRequest(
    string Titulo,
    decimal Preco,
    string? Descricao = null,
    string? Desenvolvedora = null,
    DateTime? DataLancamento = null
);
