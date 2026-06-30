using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Catalog.Infrastructure.Persistence.Repositories;

public class ItemBibliotecaRepository(CatalogDbContext contexto) : IItemBibliotecaRepository
{
    public async Task<bool> ExisteAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    ) =>
        await contexto.ItensBiblioteca.AnyAsync(
            i => i.UsuarioId == usuarioId && i.JogoId == jogoId,
            cancellationToken
        );

    public async Task<IReadOnlyList<ItemBiblioteca>> ListarPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default
    ) =>
        await contexto
            .ItensBiblioteca.AsNoTracking()
            .Where(i => i.UsuarioId == usuarioId)
            .OrderByDescending(i => i.AdicionadoEm)
            .ToListAsync(cancellationToken);

    public async Task AdicionarAsync(
        ItemBiblioteca item,
        CancellationToken cancellationToken = default
    ) => await contexto.ItensBiblioteca.AddAsync(item, cancellationToken);
}
