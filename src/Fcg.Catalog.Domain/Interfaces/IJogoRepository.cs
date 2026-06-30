using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Domain.Interfaces;

public interface IJogoRepository
{
    Task<Jogo?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AdicionarAsync(Jogo jogo, CancellationToken cancellationToken = default);
    void Atualizar(Jogo jogo);
}
