using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;

namespace Fcg.Catalog.Application.UseCases.Biblioteca;

// Lê só do modelo de leitura, sem recorrer ao banco relacional quando ele não responde: a fonte
// da verdade não tem nome de jogo nem preço, então o desvio devolveria um contrato diferente e
// degradado, e esconderia justamente a falha de projeção. Modelo de leitura não é cache.
public class ObterBibliotecaDoUsuarioUseCase(IBibliotecaReadModel bibliotecaReadModel)
{
    public async Task<IReadOnlyList<ItemBibliotecaResponse>> ExecutarAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ItemBibliotecaProjetado> itens =
            await bibliotecaReadModel.ListarPorUsuarioAsync(usuarioId, cancellationToken);

        // A consulta devolve na ordem da chave de ordenação, que é o identificador do jogo. A
        // ordem de apresentação é por aquisição mais recente e sai daqui, não do armazenamento.
        return
        [
            .. itens
                .OrderByDescending(item => item.AdquiridoEm)
                .Select(item => new ItemBibliotecaResponse(
                    item.UsuarioId,
                    item.JogoId,
                    item.PedidoId,
                    item.NomeJogo,
                    item.Preco,
                    item.AdquiridoEm
                )),
        ];
    }
}
