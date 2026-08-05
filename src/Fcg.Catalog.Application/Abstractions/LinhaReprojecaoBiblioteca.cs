namespace Fcg.Catalog.Application.Abstractions;

// Uma linha da fonte da verdade, com os três campos que vêm de outro agregado anuláveis: item sem
// pedido aprovado ou sem jogo é dado possível, e não é o adaptador quem decide o que fazer com ele.
// A linha chega como está, e quem reconstrói registra e pula.
public sealed record LinhaReprojecaoBiblioteca(
    Guid UsuarioId,
    Guid JogoId,
    DateTime AdquiridoEm,
    Guid? PedidoId,
    decimal? Preco,
    string? NomeJogo
);
