using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Persistence;

public class PedidoPersistenceTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task DevePreservarValorAoPersistirEReler()
    {
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(199.99m));
        Guid id = pedido.Id;

        await PersistirAsync(pedido);

        await using AsyncServiceScope leitura = Factory.Services.CreateAsyncScope();
        IPedidoRepository repositorio =
            leitura.ServiceProvider.GetRequiredService<IPedidoRepository>();

        Pedido? lido = await repositorio.ObterPorIdAsync(id);

        lido.Should().NotBeNull();
        lido!.Valor.Valor.Should().Be(199.99m);
    }

    [Fact]
    public async Task DeveBarrarSegundoPedidoPendenteParaOMesmoUsuarioEJogo()
    {
        var usuarioId = Guid.NewGuid();
        var jogoId = Guid.NewGuid();

        await PersistirAsync(Pedido.Criar(usuarioId, jogoId, Preco.Criar(50m)));

        Func<Task> segundoPendente = () =>
            PersistirAsync(Pedido.Criar(usuarioId, jogoId, Preco.Criar(50m)));

        // Índice único parcial ux_pedidos_usuario_jogo_pendente reforça a invariante.
        await segundoPendente.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DevePermitirNovoPendenteQuandoOAnteriorFoiRejeitado()
    {
        var usuarioId = Guid.NewGuid();
        var jogoId = Guid.NewGuid();

        var primeiro = Pedido.Criar(usuarioId, jogoId, Preco.Criar(50m));
        await PersistirAsync(primeiro);

        // Rejeitar tira o pedido do filtro (status = 0), liberando o par.
        await using (AsyncServiceScope rejeicao = Factory.Services.CreateAsyncScope())
        {
            IPedidoRepository repositorio =
                rejeicao.ServiceProvider.GetRequiredService<IPedidoRepository>();
            IUnitOfWork unitOfWork = rejeicao.ServiceProvider.GetRequiredService<IUnitOfWork>();

            Pedido? carregado = await repositorio.ObterPorIdAsync(primeiro.Id);
            carregado!.Rejeitar("Pagamento recusado");
            repositorio.Atualizar(carregado);
            await unitOfWork.SalvarAlteracoesAsync();
        }

        Func<Task> novoPendente = () =>
            PersistirAsync(Pedido.Criar(usuarioId, jogoId, Preco.Criar(50m)));

        await novoPendente.Should().NotThrowAsync();
    }

    private async Task PersistirAsync(Pedido pedido)
    {
        await using AsyncServiceScope escrita = Factory.Services.CreateAsyncScope();
        IPedidoRepository repositorio =
            escrita.ServiceProvider.GetRequiredService<IPedidoRepository>();
        IUnitOfWork unitOfWork = escrita.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repositorio.AdicionarAsync(pedido);
        await unitOfWork.SalvarAlteracoesAsync();
    }
}
