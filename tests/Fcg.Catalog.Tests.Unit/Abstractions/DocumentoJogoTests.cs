using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.Abstractions;

public class DocumentoJogoTests
{
    [Fact]
    public void DocumentoDeveCarregarOsSeisCamposDeDadosDoJogo()
    {
        DateTime lancamento = new(2015, 5, 19, 0, 0, 0, DateTimeKind.Utc);
        var jogo = Jogo.Criar(
            Titulo.Criar("The Witcher 3: Wild Hunt"),
            Preco.Criar(79.90m),
            "RPG de mundo aberto",
            "CD Projekt Red",
            lancamento
        );

        var documento = DocumentoJogo.De(jogo);

        documento
            .Should()
            .Be(
                new DocumentoJogo(
                    jogo.Id,
                    "The Witcher 3: Wild Hunt",
                    "RPG de mundo aberto",
                    79.90m,
                    "CD Projekt Red",
                    lancamento
                )
            );
    }

    [Fact]
    public void CamposOpcionaisAusentesDevemFicarNulosNoDocumento()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hades"), Preco.Criar(90m));

        var documento = DocumentoJogo.De(jogo);

        documento.Descricao.Should().BeNull();
        documento.Desenvolvedora.Should().BeNull();
        documento.DataLancamento.Should().BeNull();
    }

    // Ativo e datas de controle não fazem parte da forma indexada: o índice só guarda ativos.
    [Fact]
    public void DocumentoNaoDeveExporEstadoNemDatasDeControle()
    {
        typeof(DocumentoJogo)
            .GetProperties()
            .Select(propriedade => propriedade.Name)
            .Should()
            .BeEquivalentTo(
                nameof(DocumentoJogo.Id),
                nameof(DocumentoJogo.Titulo),
                nameof(DocumentoJogo.Descricao),
                nameof(DocumentoJogo.Preco),
                nameof(DocumentoJogo.Desenvolvedora),
                nameof(DocumentoJogo.DataLancamento)
            );
    }
}
