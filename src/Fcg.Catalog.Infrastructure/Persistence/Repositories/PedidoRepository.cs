using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Enums;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Catalog.Infrastructure.Persistence.Repositories;

public class PedidoRepository(CatalogDbContext contexto) : IPedidoRepository
{
    public async Task<bool> ExistePedidoPendenteAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    ) =>
        await contexto.Pedidos.AnyAsync(
            p =>
                p.UsuarioId == usuarioId && p.JogoId == jogoId && p.Status == StatusPedido.Pendente,
            cancellationToken
        );

    public async Task<Pedido?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    ) => await contexto.Pedidos.FindAsync([id], cancellationToken);

    public async Task AdicionarAsync(
        Pedido pedido,
        CancellationToken cancellationToken = default
    ) => await contexto.Pedidos.AddAsync(pedido, cancellationToken);

    // Nunca chama SaveChanges — o commit é do UnitOfWork no fim do use case.
    public void Atualizar(Pedido pedido) => contexto.Pedidos.Update(pedido);
}
