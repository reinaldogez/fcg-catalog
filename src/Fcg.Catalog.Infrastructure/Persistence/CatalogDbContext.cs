using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Catalog.Infrastructure.Persistence;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<Jogo> Jogos => Set<Jogo>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemBiblioteca> ItensBiblioteca => Set<ItemBiblioteca>();

    public Task SalvarAlteracoesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
