using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Application.DTOs;

public record JogoResponse(
    Guid Id,
    string Titulo,
    string? Descricao,
    decimal Preco,
    string? Desenvolvedora,
    DateTime? DataLancamento,
    bool Ativo,
    DateTime CriadoEm,
    DateTime AtualizadoEm
)
{
    public static JogoResponse De(Jogo jogo) =>
        new(
            jogo.Id,
            jogo.Titulo.Valor,
            jogo.Descricao,
            jogo.Preco.Valor,
            jogo.Desenvolvedora,
            jogo.DataLancamento,
            jogo.Ativo,
            jogo.CriadoEm,
            jogo.AtualizadoEm
        );
}
