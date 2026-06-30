using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Domain.Interfaces;

public interface IPedidoDomainService
{
    Task<Pedido> RegistrarAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    );
}
