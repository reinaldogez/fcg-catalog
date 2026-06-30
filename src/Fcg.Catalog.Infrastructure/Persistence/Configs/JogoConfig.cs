using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fcg.Catalog.Infrastructure.Persistence.Configs;

public class JogoConfig : IEntityTypeConfiguration<Jogo>
{
    public void Configure(EntityTypeBuilder<Jogo> builder)
    {
        builder.HasKey(j => j.Id);

        // VO ↔ coluna; leitura via Reconstituir (sem revalidar).
        builder
            .Property(j => j.Titulo)
            .HasConversion(t => t.Valor, valor => Titulo.Reconstituir(valor))
            .HasMaxLength(Titulo.ComprimentoMaximo)
            .IsRequired();

        builder
            .Property(j => j.Preco)
            .HasConversion(p => p.Valor, valor => Preco.Reconstituir(valor))
            .HasColumnType("numeric(18,2)")
            .IsRequired();
    }
}
