using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Biblioteca;

public class ObterBibliotecaDoUsuarioUseCase(IItemBibliotecaRepository itemBibliotecaRepository)
{
    public async Task<IReadOnlyList<ItemBibliotecaResponse>> ExecutarAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ItemBiblioteca> itens = await itemBibliotecaRepository.ListarPorUsuarioAsync(
            usuarioId,
            cancellationToken
        );

        return [.. itens.Select(ItemBibliotecaResponse.De)];
    }
}
