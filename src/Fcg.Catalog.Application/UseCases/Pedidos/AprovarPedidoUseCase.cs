using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Pedidos;

// Fecha a saga no ramo aprovado: avança o Pedido e materializa o ItemBiblioteca.
// NÃO comita nem abre transação — sob o consumer, o commit é do harness do Inbox
// (as duas escritas precisam cair numa transação única).
public class AprovarPedidoUseCase(
    IPedidoRepository pedidoRepository,
    IItemBibliotecaRepository itemBibliotecaRepository
)
{
    public async Task ExecutarAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        Pedido? pedido = await pedidoRepository.ObterPorIdAsync(orderId, cancellationToken);
        if (pedido is null)
            return;

        pedido.Aprovar();
        pedidoRepository.Atualizar(pedido);

        var item = ItemBiblioteca.Criar(pedido.UsuarioId, pedido.JogoId);
        await itemBibliotecaRepository.AdicionarAsync(item, cancellationToken);
    }
}
