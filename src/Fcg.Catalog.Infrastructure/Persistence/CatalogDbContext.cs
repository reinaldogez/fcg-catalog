using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using MassTransit;
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

        // Outbox transacional (publish atômico com o commit do agregado) + Inbox ATIVO
        // (idempotência de consumo — o catalog consome, ao contrário de serviços só-publisher).
        modelBuilder.AddTransactionalOutboxEntities();
        modelBuilder.AddInboxStateEntity();

        base.OnModelCreating(modelBuilder);
    }
}
