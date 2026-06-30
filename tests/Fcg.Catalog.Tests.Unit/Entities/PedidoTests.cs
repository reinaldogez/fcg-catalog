using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Enums;
using Fcg.Catalog.Domain.Exceptions;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.Entities;

public class PedidoTests
{
    private static Pedido CriarPedido(Preco? valor = null) =>
        Pedido.Criar(Guid.NewGuid(), Guid.NewGuid(), valor ?? Preco.Criar(100m));

    [Fact]
    public void DeveNascerPendente()
    {
        Pedido pedido = CriarPedido();

        pedido.Status.Should().Be(StatusPedido.Pendente);
    }

    [Fact]
    public void DeveAprovarPedidoPendente()
    {
        Pedido pedido = CriarPedido();

        pedido.Aprovar();

        pedido.Status.Should().Be(StatusPedido.Aprovado);
    }

    [Fact]
    public void DeveRejeitarPedidoPendenteGravandoMotivo()
    {
        Pedido pedido = CriarPedido();

        pedido.Rejeitar("Saldo insuficiente");

        pedido.Status.Should().Be(StatusPedido.Rejeitado);
        pedido.MotivoRecusa.Should().Be("Saldo insuficiente");
    }

    [Fact]
    public void NaoDeveAprovarPedidoJaAprovado()
    {
        Pedido pedido = CriarPedido();
        pedido.Aprovar();

        Action acao = () => pedido.Aprovar();

        acao.Should().Throw<DomainException>();
    }

    [Fact]
    public void NaoDeveRejeitarPedidoJaRejeitado()
    {
        Pedido pedido = CriarPedido();
        pedido.Rejeitar("motivo");

        Action acao = () => pedido.Rejeitar("outro");

        acao.Should().Throw<DomainException>();
    }

    [Fact]
    public void NaoDeveAprovarPedidoRejeitado()
    {
        Pedido pedido = CriarPedido();
        pedido.Rejeitar("motivo");

        Action acao = () => pedido.Aprovar();

        acao.Should().Throw<DomainException>();
    }

    [Fact]
    public void DeveManterSnapshotDeValorAposTransicao()
    {
        var original = Preco.Criar(250m);
        Pedido pedido = CriarPedido(original);

        pedido.Aprovar();

        pedido.Valor.Should().Be(original);
    }

    [Fact]
    public void PertenceAoDeveResponderVerdadeiroParaODono()
    {
        var dono = Guid.NewGuid();
        var pedido = Pedido.Criar(dono, Guid.NewGuid(), Preco.Criar(10m));

        pedido.PertenceAo(dono).Should().BeTrue();
        pedido.PertenceAo(Guid.NewGuid()).Should().BeFalse();
    }
}
