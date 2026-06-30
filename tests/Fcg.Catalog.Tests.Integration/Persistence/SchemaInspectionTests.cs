using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Persistence;

public class SchemaInspectionTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task TabelasEColunasDevemEstarEmSnakeCase()
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        List<string> tabelas = await db
            .Database.SqlQueryRaw<string>(
                "SELECT table_name::text AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'"
            )
            .ToListAsync();

        tabelas.Should().Contain(["jogos", "pedidos", "itens_biblioteca"]);

        List<string> colunasPedido = await db
            .Database.SqlQueryRaw<string>(
                "SELECT column_name::text AS \"Value\" FROM information_schema.columns WHERE table_name = 'pedidos'"
            )
            .ToListAsync();

        colunasPedido.Should().Contain(["usuario_id", "jogo_id", "criado_em"]);
    }

    [Fact]
    public async Task IndiceParcialDePedidoDeveTerFiltroStatusZero()
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        string definicao = await db
            .Database.SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ux_pedidos_usuario_jogo_pendente'"
            )
            .SingleAsync();

        definicao.Should().Contain("status = 0");
    }
}
