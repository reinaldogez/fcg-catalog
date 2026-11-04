using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Application.Abstractions;

// Forma do jogo no índice de busca: só os campos de dados. Ativo fica de fora porque o índice
// guarda apenas jogos ativos, e as datas de controle não servem à busca.
public record DocumentoJogo(
    Guid Id,
    string Titulo,
    string? Descricao,
    decimal Preco,
    string? Desenvolvedora,
    DateTime? DataLancamento
)
{
    // A função única jogo → documento. Quem indexa a partir do evento e quem reconstrói a partir
    // da fonte da verdade passam por aqui; duas conversões divergiriam no primeiro campo novo.
    public static DocumentoJogo De(Jogo jogo) =>
        new(
            jogo.Id,
            jogo.Titulo.Valor,
            jogo.Descricao,
            jogo.Preco.Valor,
            jogo.Desenvolvedora,
            jogo.DataLancamento
        );
}
