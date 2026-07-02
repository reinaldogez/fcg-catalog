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

public class AprovarPedidoUseCaseTests
{
    private readonly Mock<IPedidoRepository> _pedidoRepository = new();
    private readonly Mock<IItemBibliotecaRepository> _itemBibliotecaRepository = new();
    private readonly AprovarPedidoUseCase _useCase;

    public AprovarPedidoUseCaseTests()
    {
        _useCase = new AprovarPedidoUseCase(
            _pedidoRepository.Object,
            _itemBibliotecaRepository.Object,
            NullLogger<AprovarPedidoUseCase>.Instance
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

        pedido.Status.Should().Be(StatusPedido.Aprovado);
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
    public async Task NaoDeveComitar()
    {
        // A armadilha do consumer: o commit é do harness do Inbox, não do use case. Complementa a
        // guarda estrutural provando em execução — um IUnitOfWork estrito falha se tocado, e o use
        // case não tem como comitar porque nem sequer recebe a dependência.
        var mockUnitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(90m));
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(pedido.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pedido);

        await _useCase.ExecutarAsync(pedido.Id, CancellationToken.None);

        mockUnitOfWork.Verify(
            u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task PedidoJaTerminalEhNoOpSemExcecao()
    {
        // E1 — redelivery/duplicata para pedido já decidido: no-op benigno, nunca exceção nem
        // segunda escrita (senão a unique de itens_biblioteca estouraria sob reentrega).
        var pedido = Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), Preco.Criar(90m));
        pedido.Aprovar();
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(pedido.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pedido);

        Func<Task> acao = () => _useCase.ExecutarAsync(pedido.Id, CancellationToken.None);

        await acao.Should().NotThrowAsync();
        pedido.Status.Should().Be(StatusPedido.Aprovado);
        _pedidoRepository.Verify(r => r.Atualizar(It.IsAny<Pedido>()), Times.Never);
        _itemBibliotecaRepository.Verify(
            r => r.AdicionarAsync(It.IsAny<ItemBiblioteca>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task OrderInexistenteEhNoOpSemExcecao()
    {
        // E2 — OrderId ausente: descarta com ACK (loga e retorna), sem lançar nack.
        _pedidoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pedido?)null);

        Func<Task> acao = () => _useCase.ExecutarAsync(Guid.NewGuid(), CancellationToken.None);

        await acao.Should().NotThrowAsync();
        _pedidoRepository.Verify(r => r.Atualizar(It.IsAny<Pedido>()), Times.Never);
        _itemBibliotecaRepository.Verify(
            r => r.AdicionarAsync(It.IsAny<ItemBiblioteca>(), It.IsAny<CancellationToken>()),
            Times.Never
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
