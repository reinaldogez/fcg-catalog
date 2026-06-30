using Fcg.Catalog.Domain.Exceptions;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.ValueObjects;

public class TituloTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeveRejeitarTituloVazio(string? valor)
    {
        Action acao = () => Titulo.Criar(valor!);

        acao.Should().Throw<DomainException>();
    }

    [Fact]
    public void DeveRejeitarTituloAcimaDoLimite()
    {
        string longo = new('a', Titulo.ComprimentoMaximo + 1);

        Action acao = () => Titulo.Criar(longo);

        acao.Should().Throw<DomainException>();
    }

    [Fact]
    public void DeveAceitarTituloNoLimite()
    {
        string noLimite = new('a', Titulo.ComprimentoMaximo);

        var titulo = Titulo.Criar(noLimite);

        titulo.Valor.Should().HaveLength(Titulo.ComprimentoMaximo);
    }

    [Fact]
    public void DeveAplicarTrim()
    {
        var titulo = Titulo.Criar("  The Witcher 3  ");

        titulo.Valor.Should().Be("The Witcher 3");
    }

    [Fact]
    public void DeveReconstituirSemValidar()
    {
        var titulo = Titulo.Reconstituir("");

        titulo.Valor.Should().Be("");
    }
}
