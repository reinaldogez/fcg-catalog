using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Catalog.Infrastructure.Persistence.Repositories;

public class JogoRepository(CatalogDbContext contexto) : IJogoRepository
{
    public async Task<Jogo?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    ) => await contexto.Jogos.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<Jogo>> ListarAsync(
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default
    ) =>
        await contexto
            .Jogos.AsNoTracking()
            .OrderBy(j => j.CriadoEm)
            .ThenBy(j => j.Id)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(cancellationToken);

    public async Task AdicionarAsync(Jogo jogo, CancellationToken cancellationToken = default) =>
        await contexto.Jogos.AddAsync(jogo, cancellationToken);

    // Nunca chama SaveChanges — o commit é do UnitOfWork no fim do use case.
    public void Atualizar(Jogo jogo) => contexto.Jogos.Update(jogo);
}
