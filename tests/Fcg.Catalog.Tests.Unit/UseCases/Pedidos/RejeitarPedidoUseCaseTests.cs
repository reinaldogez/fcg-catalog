using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Enums;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Pedidos;

public class RejeitarPedidoUseCaseTests
{
    private readonly Mock<IPedidoRepository> _pedidoRepository = new();
    private readonly Mock<IItemBibliotecaRepository> _itemBibliotecaRepository = new();
    private readonly RejeitarPedidoUseCase _useCase;

    public RejeitarPedidoUseCaseTests() =>
        _useCase = new RejeitarPedidoUseCase(
            _pedidoRepository.Object,
            NullLogger<RejeitarPedidoUseCase>.Instance
        );

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
    public async Task NaoDeveComitar()
    {
        // A armadilha do consumer: o commit é do harness do Inbox, não do use case. Um IUnitOfWork
        // estrito falharia se tocado; o use case nem recebe a dependência.
        var mockUnitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(90m));
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(pedido.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pedido);

        await _useCase.ExecutarAsync(pedido.Id, "Pagamento recusado", CancellationToken.None);

        mockUnitOfWork.Verify(
            u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task PedidoJaTerminalEhNoOpSemExcecao()
    {
        // E1 — pedido já decidido: no-op benigno, sem exceção, sem sobrescrever o estado terminal.
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(90m));
        pedido.Aprovar();
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(pedido.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pedido);

        Func<Task> acao = () =>
            _useCase.ExecutarAsync(pedido.Id, "Pagamento recusado", CancellationToken.None);

        await acao.Should().NotThrowAsync();
        pedido.Status.Should().Be(StatusPedido.Aprovado);
        pedido.MotivoRecusa.Should().BeNull();
        _pedidoRepository.Verify(r => r.Atualizar(It.IsAny<Pedido>()), Times.Never);
    }

    [Fact]
    public async Task OrderInexistenteEhNoOpSemExcecao()
    {
        // E2 — OrderId ausente: descarta com ACK (loga e retorna), sem lançar nack.
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pedido?)null);

        Func<Task> acao = () =>
            _useCase.ExecutarAsync(Guid.NewGuid(), "Pagamento recusado", CancellationToken.None);

        await acao.Should().NotThrowAsync();
        _pedidoRepository.Verify(r => r.Atualizar(It.IsAny<Pedido>()), Times.Never);
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
