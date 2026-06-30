using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Pedidos;

public class ObterPedidoPorIdUseCaseTests
{
    private readonly Mock<IPedidoRepository> _pedidoRepository = new();
    private readonly ObterPedidoPorIdUseCase _useCase;

    public ObterPedidoPorIdUseCaseTests() =>
        _useCase = new ObterPedidoPorIdUseCase(_pedidoRepository.Object);

    [Fact]
    public async Task DeveMapearOPedidoEncontrado()
    {
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(60m));
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(pedido.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pedido);

        PedidoResponse? response = await _useCase.ExecutarAsync(pedido.Id, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Id.Should().Be(pedido.Id);
        response.Valor.Should().Be(60m);
        response.Status.Should().Be("Pendente");
    }

    [Fact]
    public async Task DeveRetornarNuloQuandoNaoEncontrado()
    {
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pedido?)null);

        PedidoResponse? response = await _useCase.ExecutarAsync(
            Guid.NewGuid(),
            CancellationToken.None
        );

        response.Should().BeNull();
    }
}
