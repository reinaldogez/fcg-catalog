using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Exceptions;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.Services;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.Services;

public class PedidoDomainServiceTests
{
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly Mock<IItemBibliotecaRepository> _itemBibliotecaRepository = new();
    private readonly Mock<IPedidoRepository> _pedidoRepository = new();
    private readonly PedidoDomainService _service;

    public PedidoDomainServiceTests()
    {
        _service = new PedidoDomainService(
            _jogoRepository.Object,
            _itemBibliotecaRepository.Object,
            _pedidoRepository.Object
        );
    }

    private void ConfigurarJogo(Jogo? jogo)
    {
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);
    }

    private void ConfigurarPosse(bool possui)
    {
        _itemBibliotecaRepository
            .Setup(r =>
                r.ExisteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(possui);
    }

    private void ConfigurarPendente(bool pendente)
    {
        _pedidoRepository
            .Setup(r =>
                r.ExistePedidoPendenteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(pendente);
    }

    [Fact]
    public async Task DeveCriarPedidoComSnapshotDoPrecoDoJogo()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Celeste"), Preco.Criar(75m));
        ConfigurarJogo(jogo);
        ConfigurarPosse(false);
        ConfigurarPendente(false);
        var usuarioId = Guid.NewGuid();

        (Pedido pedido, string tituloJogo) = await _service.RegistrarAsync(
            usuarioId,
            jogo.Id,
            CancellationToken.None
        );

        pedido.UsuarioId.Should().Be(usuarioId);
        pedido.JogoId.Should().Be(jogo.Id);
        pedido.Valor.Should().Be(jogo.Preco);
        tituloJogo.Should().Be("Celeste");
    }

    [Fact]
    public async Task DeveBarrarJogoInexistente()
    {
        ConfigurarJogo(null);

        Func<Task> acao = () =>
            _service.RegistrarAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        await acao.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task DeveBarrarJogoInativo()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Limbo"), Preco.Criar(40m));
        jogo.Desativar();
        ConfigurarJogo(jogo);

        Func<Task> acao = () =>
            _service.RegistrarAsync(Guid.NewGuid(), jogo.Id, CancellationToken.None);

        await acao.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task DeveBarrarQuandoUsuarioJaPossuiOJogo()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Inside"), Preco.Criar(60m));
        ConfigurarJogo(jogo);
        ConfigurarPosse(true);

        Func<Task> acao = () =>
            _service.RegistrarAsync(Guid.NewGuid(), jogo.Id, CancellationToken.None);

        await acao.Should().ThrowAsync<DomainConflictException>();
    }

    [Fact]
    public async Task DeveBarrarPedidoPendenteDuplicado()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hollow Knight"), Preco.Criar(50m));
        ConfigurarJogo(jogo);
        ConfigurarPosse(false);
        ConfigurarPendente(true);

        Func<Task> acao = () =>
            _service.RegistrarAsync(Guid.NewGuid(), jogo.Id, CancellationToken.None);

        await acao.Should().ThrowAsync<DomainConflictException>();
    }

    [Fact]
    public async Task DevePermitirNovoPedidoAposRejeitado()
    {
        // Pedido rejeitado é terminal → não conta como pendente → novo pedido é permitido.
        var jogo = Jogo.Criar(Titulo.Criar("Gris"), Preco.Criar(45m));
        ConfigurarJogo(jogo);
        ConfigurarPosse(false);
        ConfigurarPendente(false);

        (Pedido pedido, _) = await _service.RegistrarAsync(
            Guid.NewGuid(),
            jogo.Id,
            CancellationToken.None
        );

        pedido.Should().NotBeNull();
    }
}
