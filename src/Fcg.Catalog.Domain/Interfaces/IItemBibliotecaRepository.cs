using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Domain.Interfaces;

public interface IItemBibliotecaRepository
{
    Task<bool> ExisteAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    );
    Task AdicionarAsync(ItemBiblioteca item, CancellationToken cancellationToken = default);
}
