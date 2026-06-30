using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Domain.Interfaces;

public interface IPedidoRepository
{
    Task<bool> ExistePedidoPendenteAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    );
    Task<Pedido?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AdicionarAsync(Pedido pedido, CancellationToken cancellationToken = default);
    void Atualizar(Pedido pedido);
}
