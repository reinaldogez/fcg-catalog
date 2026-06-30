namespace Fcg.Catalog.Application.DTOs;

public record CriarJogoRequest(
    string Titulo,
    decimal Preco,
    string? Descricao = null,
    string? Desenvolvedora = null,
    DateTime? DataLancamento = null
);
