using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Application.DTOs;

public record PedidoResponse(
    Guid Id,
    Guid UsuarioId,
    Guid JogoId,
    decimal Valor,
    string Status,
    string? MotivoRecusa,
    DateTime CriadoEm
)
{
    public static PedidoResponse De(Pedido pedido) =>
        new(
            pedido.Id,
            pedido.UsuarioId,
            pedido.JogoId,
            pedido.Valor.Valor,
            pedido.Status.ToString(),
            pedido.MotivoRecusa,
            pedido.CriadoEm
        );
}
