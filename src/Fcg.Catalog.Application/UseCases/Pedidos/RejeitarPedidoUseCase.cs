using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Pedidos;

// Fecha a saga no ramo rejeitado: grava o motivo no Pedido.
// Não toca ItemBiblioteca e NÃO comita — o commit é do harness do Inbox.
public class RejeitarPedidoUseCase(IPedidoRepository pedidoRepository)
{
    public async Task ExecutarAsync(
        Guid orderId,
        string motivo,
        CancellationToken cancellationToken = default
    )
    {
        Pedido? pedido = await pedidoRepository.ObterPorIdAsync(orderId, cancellationToken);
        if (pedido is null)
            return;

        pedido.Rejeitar(motivo);
        pedidoRepository.Atualizar(pedido);
    }
}
