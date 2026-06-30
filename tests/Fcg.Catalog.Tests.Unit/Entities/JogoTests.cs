using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.Entities;

public class JogoTests
{
    private static Jogo CriarJogo() => Jogo.Criar(Titulo.Criar("Hades"), Preco.Criar(90m));

    [Fact]
    public void DeveCriarJogoAtivo()
    {
        Jogo jogo = CriarJogo();

        jogo.Ativo.Should().BeTrue();
        jogo.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void DeveDesativarJogo()
    {
        Jogo jogo = CriarJogo();

        jogo.Desativar();

        jogo.Ativo.Should().BeFalse();
    }

    [Fact]
    public void DeveAtualizarEscalaresMantendoAtivo()
    {
        Jogo jogo = CriarJogo();

        jogo.Atualizar(Titulo.Criar("Hades II"), Preco.Criar(120m), descricao: "Sequência");

        jogo.Titulo.Valor.Should().Be("Hades II");
        jogo.Preco.Valor.Should().Be(120m);
        jogo.Descricao.Should().Be("Sequência");
        jogo.Ativo.Should().BeTrue();
    }
}
