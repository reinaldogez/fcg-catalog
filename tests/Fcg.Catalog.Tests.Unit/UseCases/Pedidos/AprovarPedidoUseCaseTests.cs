using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Pedidos;

public class AprovarPedidoUseCaseTests
{
    private readonly Mock<IPedidoRepository> _pedidoRepository = new();
    private readonly Mock<IItemBibliotecaRepository> _itemBibliotecaRepository = new();
    private readonly AprovarPedidoUseCase _useCase;

    public AprovarPedidoUseCaseTests()
    {
        _useCase = new AprovarPedidoUseCase(
            _pedidoRepository.Object,
            _itemBibliotecaRepository.Object
        );
    }

    [Fact]
    public async Task DeveAprovarECriarItemBiblioteca()
    {
        var usuarioId = Guid.NewGuid();
        var jogoId = Guid.NewGuid();
        var pedido = Pedido.Criar(usuarioId, jogoId, Preco.Criar(90m));
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(pedido.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pedido);

        await _useCase.ExecutarAsync(pedido.Id, CancellationToken.None);

        pedido.Status.Should().Be(Domain.Enums.StatusPedido.Aprovado);
        _itemBibliotecaRepository.Verify(
            r =>
                r.AdicionarAsync(
                    It.Is<ItemBiblioteca>(i => i.UsuarioId == usuarioId && i.JogoId == jogoId),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public void NaoDeveDependerDeUnitOfWork()
    {
        // O commit do ramo de consumo é do harness do Inbox. Injetar IUnitOfWork abriria
        // caminho para um SaveChanges aninhado que comitaria fora da transação única —
        // exatamente o bug que só aparece sob redelivery. Guarda estrutural contra isso.
        bool dependeDeUnitOfWork = typeof(AprovarPedidoUseCase)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(IUnitOfWork));

        dependeDeUnitOfWork.Should().BeFalse();
    }
}
