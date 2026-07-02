using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Enums;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Application.UseCases.Pedidos;

// Fecha a saga no ramo aprovado: avança o Pedido e materializa o ItemBiblioteca.
// NÃO comita nem abre transação — sob o consumer, o commit é do harness do Inbox
// (as duas escritas precisam cair numa transação única).
public class AprovarPedidoUseCase(
    IPedidoRepository pedidoRepository,
    IItemBibliotecaRepository itemBibliotecaRepository,
    ILogger<AprovarPedidoUseCase> logger
)
{
    public async Task ExecutarAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        Pedido? pedido = await pedidoRepository.ObterPorIdAsync(orderId, cancellationToken);
        if (pedido is null)
        {
            // Order inexistente: o catalog gera o OrderId, então não deveria ocorrer. Reentregar
            // algo que nunca existirá é inútil — loga e descarta (retorno normal → ACK).
            logger.LogWarning(
                "PaymentProcessed aprovado para pedido inexistente {OrderId}; descartando.",
                orderId
            );
            return;
        }

        if (pedido.Status != StatusPedido.Pendente)
        {
            // Pedido já terminal: redelivery/duplicata benigna. Reprocessar não corrige e um
            // erro poluiria a DLQ com não-falha — no-op com ACK. A invariante de transição do
            // agregado segue a verdade;
            logger.LogWarning(
                "PaymentProcessed aprovado para pedido {OrderId} já em estado {Status}; ignorando.",
                orderId,
                pedido.Status
            );
            return;
        }

        pedido.Aprovar();
        pedidoRepository.Atualizar(pedido);

        var item = ItemBiblioteca.Criar(pedido.UsuarioId, pedido.JogoId);
        await itemBibliotecaRepository.AdicionarAsync(item, cancellationToken);
    }
}
