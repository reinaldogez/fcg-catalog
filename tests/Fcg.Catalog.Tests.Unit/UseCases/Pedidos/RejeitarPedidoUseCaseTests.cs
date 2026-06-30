using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Enums;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Pedidos;

public class RejeitarPedidoUseCaseTests
{
    private readonly Mock<IPedidoRepository> _pedidoRepository = new();
    private readonly Mock<IItemBibliotecaRepository> _itemBibliotecaRepository = new();
    private readonly RejeitarPedidoUseCase _useCase;

    public RejeitarPedidoUseCaseTests() =>
        _useCase = new RejeitarPedidoUseCase(_pedidoRepository.Object);

    [Fact]
    public async Task DeveGravarOMotivoSemTocarABiblioteca()
    {
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(90m));
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(pedido.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pedido);

        await _useCase.ExecutarAsync(pedido.Id, "Pagamento recusado", CancellationToken.None);

        pedido.Status.Should().Be(StatusPedido.Rejeitado);
        pedido.MotivoRecusa.Should().Be("Pagamento recusado");
        _itemBibliotecaRepository.Verify(
            r => r.AdicionarAsync(It.IsAny<ItemBiblioteca>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public void NaoDeveDependerDeUnitOfWork()
    {
        // Mesma guarda do ramo aprovado: o commit é do harness do Inbox, não do use case.
        bool dependeDeUnitOfWork = typeof(RejeitarPedidoUseCase)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(IUnitOfWork));

        dependeDeUnitOfWork.Should().BeFalse();
    }
}
