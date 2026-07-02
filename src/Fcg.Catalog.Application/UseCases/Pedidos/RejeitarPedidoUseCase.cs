using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Enums;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Application.UseCases.Pedidos;

// Fecha a saga no ramo rejeitado: grava o motivo no Pedido.
// Não toca ItemBiblioteca e NÃO comita — o commit é do harness do Inbox.
public class RejeitarPedidoUseCase(
    IPedidoRepository pedidoRepository,
    ILogger<RejeitarPedidoUseCase> logger
)
{
    public async Task ExecutarAsync(
        Guid orderId,
        string motivo,
        CancellationToken cancellationToken = default
    )
    {
        Pedido? pedido = await pedidoRepository.ObterPorIdAsync(orderId, cancellationToken);
        if (pedido is null)
        {
            // Order inexistente: loga e descarta (retorno normal → ACK). Reentregar algo que
            // nunca existirá é inútil.
            logger.LogWarning(
                "PaymentProcessed rejeitado para pedido inexistente {OrderId}; descartando.",
                orderId
            );
            return;
        }

        if (pedido.Status != StatusPedido.Pendente)
        {
            // Pedido já terminal: redelivery/duplicata benigna → no-op com ACK, nunca erro/DLQ.
            logger.LogWarning(
                "PaymentProcessed rejeitado para pedido {OrderId} já em estado {Status}; ignorando.",
                orderId,
                pedido.Status
            );
            return;
        }

        pedido.Rejeitar(motivo);
        pedidoRepository.Atualizar(pedido);
    }
}
