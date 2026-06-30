using Fcg.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fcg.Catalog.Infrastructure.Persistence.Configs;

public class ItemBibliotecaConfig : IEntityTypeConfiguration<ItemBiblioteca>
{
    public void Configure(EntityTypeBuilder<ItemBiblioteca> builder)
    {
        builder.HasKey(i => i.Id);

        // Único por par (usuario, jogo) — não parcial.
        builder
            .HasIndex(i => new { i.UsuarioId, i.JogoId })
            .IsUnique()
            .HasDatabaseName("ux_itens_biblioteca_usuario_jogo");
    }
}
