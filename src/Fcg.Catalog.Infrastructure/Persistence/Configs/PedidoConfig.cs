using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fcg.Catalog.Infrastructure.Persistence.Configs;

public class PedidoConfig : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.HasKey(p => p.Id);

        // Snapshot imutável de preço; mesmo VO/coluna que Jogo.Preco.
        builder
            .Property(p => p.Valor)
            .HasConversion(v => v.Valor, valor => Preco.Reconstituir(valor))
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        // Enum persistido como int ordinal (default EF) — valores append-only.
        builder.Property(p => p.Status).IsRequired();

        // Invariante "≤1 pedido pendente por (usuario, jogo)" reforçada no banco.
        // Pedidos aprovados/rejeitados ficam fora do filtro: retry após rejeição é permitido.
        builder
            .HasIndex(p => new { p.UsuarioId, p.JogoId })
            .IsUnique()
            .HasFilter("status = 0")
            .HasDatabaseName("ux_pedidos_usuario_jogo_pendente");
    }
}
