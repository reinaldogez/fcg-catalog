using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Infrastructure.Seed;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Seed;

public class CatalogSeederTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task DeveSemearCatalogoComParDePrecosAoRedorDoThresholdQuandoBaseVazia()
    {
        await using (AsyncServiceScope escrita = Factory.Services.CreateAsyncScope())
        {
            CatalogSeeder seeder = escrita.ServiceProvider.GetRequiredService<CatalogSeeder>();
            await seeder.SeedAsync();
        }

        await using AsyncServiceScope leitura = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = leitura.ServiceProvider.GetRequiredService<CatalogDbContext>();
        List<Jogo> jogos = await db.Jogos.AsNoTracking().ToListAsync();

        jogos.Should().NotBeEmpty();
        // Par obrigatório ao redor do threshold do pagamento simulado — ramos aprovado e rejeitado.
        jogos.Should().Contain(j => j.Preco.Valor > 5000m);
        jogos.Should().Contain(j => j.Preco.Valor < 5000m);
    }

    [Fact]
    public async Task DeveSerNoOpQuandoBaseNaoVazia()
    {
        await using (AsyncServiceScope primeira = Factory.Services.CreateAsyncScope())
        {
            await primeira.ServiceProvider.GetRequiredService<CatalogSeeder>().SeedAsync();
        }

        int contagemInicial;
        await using (AsyncServiceScope contagem = Factory.Services.CreateAsyncScope())
        {
            CatalogDbContext db = contagem.ServiceProvider.GetRequiredService<CatalogDbContext>();
            contagemInicial = await db.Jogos.CountAsync();
        }

        // Segunda execução em base já populada: idempotência por presença → no-op.
        await using (AsyncServiceScope segunda = Factory.Services.CreateAsyncScope())
        {
            await segunda.ServiceProvider.GetRequiredService<CatalogSeeder>().SeedAsync();
        }

        await using AsyncServiceScope final = Factory.Services.CreateAsyncScope();
        CatalogDbContext dbFinal = final.ServiceProvider.GetRequiredService<CatalogDbContext>();
        int contagemFinal = await dbFinal.Jogos.CountAsync();

        contagemFinal.Should().Be(contagemInicial);
    }
}
