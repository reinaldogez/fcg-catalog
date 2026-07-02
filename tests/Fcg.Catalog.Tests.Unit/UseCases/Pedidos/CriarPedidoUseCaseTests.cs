using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Exceptions;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Pedidos;

public class CriarPedidoUseCaseTests
{
    private readonly Mock<IPedidoDomainService> _domainService = new();
    private readonly Mock<IPedidoRepository> _pedidoRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPublishEndpoint> _publishEndpoint = new();
    private readonly CriarPedidoUseCase _useCase;

    public CriarPedidoUseCaseTests()
    {
        _useCase = new CriarPedidoUseCase(
            _domainService.Object,
            _pedidoRepository.Object,
            _unitOfWork.Object,
            _publishEndpoint.Object
        );
    }

    [Fact]
    public async Task DevePublicarAntesDoCommit()
    {
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(120m));
        _domainService
            .Setup(s =>
                s.RegistrarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((pedido, "Celeste"));

        // Grava a ordem real das chamadas — a prova de atomicidade do Outbox.
        List<string> ordem = [];
        _publishEndpoint
            .Setup(p => p.Publish(It.IsAny<OrderPlacedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("publish"))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("commit"))
            .Returns(Task.CompletedTask);

        await _useCase.ExecutarAsync(
            new CriarPedidoRequest(pedido.JogoId),
            pedido.UsuarioId,
            "ana@exemplo.com",
            "Ana",
            CancellationToken.None
        );

        ordem.Should().Equal("publish", "commit");
    }

    [Fact]
    public async Task DevePublicarEventoComGameNameEPrecoSnapshot()
    {
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(99.90m));
        _domainService
            .Setup(s =>
                s.RegistrarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((pedido, "Hollow Knight"));

        OrderPlacedEvent? publicado = null;
        _publishEndpoint
            .Setup(p => p.Publish(It.IsAny<OrderPlacedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrderPlacedEvent, CancellationToken>((evento, _) => publicado = evento)
            .Returns(Task.CompletedTask);

        await _useCase.ExecutarAsync(
            new CriarPedidoRequest(pedido.JogoId),
            pedido.UsuarioId,
            "ana@exemplo.com",
            "Ana",
            CancellationToken.None
        );

        publicado.Should().NotBeNull();
        publicado!.OrderId.Should().Be(pedido.Id);
        publicado.GameId.Should().Be(pedido.JogoId);
        publicado.GameName.Should().Be("Hollow Knight");
        publicado.Price.Should().Be(pedido.Valor.Valor);
        publicado.UserEmail.Should().Be("ana@exemplo.com");
        publicado.UserName.Should().Be("Ana");
    }

    [Fact]
    public async Task NaoDevePublicarNemComitarQuandoInvarianteFalha()
    {
        _domainService
            .Setup(s =>
                s.RegistrarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(
                new DomainConflictException("Já existe um pedido pendente para este jogo.")
            );

        Func<Task> acao = () =>
            _useCase.ExecutarAsync(
                new CriarPedidoRequest(Guid.NewGuid()),
                Guid.NewGuid(),
                "ana@exemplo.com",
                "Ana",
                CancellationToken.None
            );

        await acao.Should().ThrowAsync<DomainConflictException>();
        _publishEndpoint.Verify(
            p => p.Publish(It.IsAny<OrderPlacedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        _unitOfWork.Verify(
            u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
