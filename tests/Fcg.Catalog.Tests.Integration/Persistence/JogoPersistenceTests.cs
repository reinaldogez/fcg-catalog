using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Persistence;

public class JogoPersistenceTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task DevePreservarTituloEPrecoAoPersistirEReler()
    {
        var jogo = Jogo.Criar(
            Titulo.Criar("Hollow Knight"),
            Preco.Criar(49.90m),
            "Metroidvania aclamado"
        );
        Guid id = jogo.Id;

        await using (AsyncServiceScope escrita = Factory.Services.CreateAsyncScope())
        {
            IJogoRepository repositorio =
                escrita.ServiceProvider.GetRequiredService<IJogoRepository>();
            IUnitOfWork unitOfWork = escrita.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await repositorio.AdicionarAsync(jogo);
            await unitOfWork.SalvarAlteracoesAsync();
        }

        await using AsyncServiceScope leitura = Factory.Services.CreateAsyncScope();
        IJogoRepository repositorioLeitura =
            leitura.ServiceProvider.GetRequiredService<IJogoRepository>();

        Jogo? lido = await repositorioLeitura.ObterPorIdAsync(id);

        lido.Should().NotBeNull();
        // VOs materializados via Reconstituir preservam o valor.
        lido!.Titulo.Valor.Should().Be("Hollow Knight");
        lido.Preco.Valor.Should().Be(49.90m);
        lido.Ativo.Should().BeTrue();
    }
}
