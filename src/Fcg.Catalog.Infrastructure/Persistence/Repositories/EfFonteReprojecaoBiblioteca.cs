using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Catalog.Infrastructure.Persistence.Repositories;

// Junta o item de biblioteca ao pedido aprovado do mesmo par e ao jogo. As duas junções são à
// esquerda de propósito: a linha sem contraparte precisa chegar a quem reconstrói para ser
// contabilizada, e a junção interna a faria sumir em silêncio. Não há filtro por jogo ativo — o
// jogo desativado não sai de bibliotecas existentes.
public class EfFonteReprojecaoBiblioteca(CatalogDbContext contexto) : IFonteReprojecaoBiblioteca
{
    public async Task<IReadOnlyList<LinhaReprojecaoBiblioteca>> LerPaginaAsync(
        int deslocamento,
        int tamanho,
        CancellationToken cancellationToken = default
    )
    {
        // A ordenação pela chave do item é o que torna a paginação por deslocamento determinística.
        var pagina = await (
            from item in contexto.ItensBiblioteca
            join pedido in contexto.Pedidos.Where(p => p.Status == StatusPedido.Aprovado)
                on new { item.UsuarioId, item.JogoId } equals new
                {
                    pedido.UsuarioId,
                    pedido.JogoId,
                }
                into pedidosDoPar
            from pedidoAprovado in pedidosDoPar.DefaultIfEmpty()
            join jogo in contexto.Jogos on item.JogoId equals jogo.Id into jogosDoItem
            from jogoDoItem in jogosDoItem.DefaultIfEmpty()
            orderby item.Id
            select new
            {
                Item = item,
                Pedido = pedidoAprovado,
                Jogo = jogoDoItem,
            }
        )
            .AsNoTracking()
            .Skip(deslocamento)
            .Take(tamanho)
            .ToListAsync(cancellationToken);

        return
        [
            .. pagina.Select(linha => new LinhaReprojecaoBiblioteca(
                linha.Item.UsuarioId,
                linha.Item.JogoId,
                linha.Item.AdicionadoEm,
                linha.Pedido?.Id,
                // O preço é o do pedido, não o do catálogo: é o valor pago, e o catálogo pode ter
                // mudado desde então.
                linha.Pedido?.Valor.Valor,
                linha.Jogo?.Titulo.Valor
            )),
        ];
    }
}
