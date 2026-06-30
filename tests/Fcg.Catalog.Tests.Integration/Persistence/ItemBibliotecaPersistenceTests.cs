using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Persistence;

public class ItemBibliotecaPersistenceTests(CatalogApiFactory factory)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task DeveBarrarItemDuplicadoParaOMesmoUsuarioEJogo()
    {
        var usuarioId = Guid.NewGuid();
        var jogoId = Guid.NewGuid();

        await PersistirAsync(ItemBiblioteca.Criar(usuarioId, jogoId));

        Func<Task> duplicado = () => PersistirAsync(ItemBiblioteca.Criar(usuarioId, jogoId));

        // Índice único ux_itens_biblioteca_usuario_jogo barra o par repetido.
        await duplicado.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task PersistirAsync(ItemBiblioteca item)
    {
        await using AsyncServiceScope escrita = Factory.Services.CreateAsyncScope();
        IItemBibliotecaRepository repositorio =
            escrita.ServiceProvider.GetRequiredService<IItemBibliotecaRepository>();
        IUnitOfWork unitOfWork = escrita.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repositorio.AdicionarAsync(item);
        await unitOfWork.SalvarAlteracoesAsync();
    }
}
