using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Pedidos;

public class ObterPedidoPorIdUseCase(IPedidoRepository pedidoRepository)
{
    public async Task<PedidoResponse?> ExecutarAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        Pedido? pedido = await pedidoRepository.ObterPorIdAsync(id, cancellationToken);
        return pedido is null ? null : PedidoResponse.De(pedido);
    }

    // Entrega a entidade para a autorização baseada em recurso (o handler consome pedido.PertenceAo).
    public Task<Pedido?> ObterEntidadeAsync(
        Guid id,
        CancellationToken cancellationToken = default
    ) => pedidoRepository.ObterPorIdAsync(id, cancellationToken);
}
