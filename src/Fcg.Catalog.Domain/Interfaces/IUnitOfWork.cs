namespace Fcg.Catalog.Domain.Interfaces;

public interface IUnitOfWork
{
    Task SalvarAlteracoesAsync(CancellationToken cancellationToken = default);
}
