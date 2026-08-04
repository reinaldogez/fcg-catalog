using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Biblioteca;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Biblioteca;

public class ObterBibliotecaDoUsuarioUseCaseTests
{
    private static readonly DateTime s_instante = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IBibliotecaReadModel> _bibliotecaReadModel = new();
    private readonly ObterBibliotecaDoUsuarioUseCase _useCase;

    public ObterBibliotecaDoUsuarioUseCaseTests() =>
        _useCase = new ObterBibliotecaDoUsuarioUseCase(_bibliotecaReadModel.Object);

    [Fact]
    public async Task DeveMapearOsSeisCamposDoItemProjetado()
    {
        var usuarioId = Guid.NewGuid();
        ItemBibliotecaProjetado projetado = ItemDe(usuarioId, s_instante);
        Responder(usuarioId, [projetado]);

        IReadOnlyList<ItemBibliotecaResponse> resultado = await _useCase.ExecutarAsync(
            usuarioId,
            CancellationToken.None
        );

        resultado
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new ItemBibliotecaResponse(
                    projetado.UsuarioId,
                    projetado.JogoId,
                    projetado.PedidoId,
                    projetado.NomeJogo,
                    projetado.Preco,
                    projetado.AdquiridoEm
                )
            );
    }

    [Fact]
    public async Task DeveOrdenarPelaAquisicaoMaisRecente()
    {
        var usuarioId = Guid.NewGuid();
        ItemBibliotecaProjetado antigo = ItemDe(usuarioId, s_instante);
        ItemBibliotecaProjetado recente = ItemDe(usuarioId, s_instante.AddDays(1));

        // A porta devolve na ordem da chave de ordenação, que nada tem a ver com a data: a ordem
        // de apresentação só existe se o caso de uso a aplicar.
        Responder(usuarioId, [antigo, recente]);

        IReadOnlyList<ItemBibliotecaResponse> resultado = await _useCase.ExecutarAsync(
            usuarioId,
            CancellationToken.None
        );

        resultado.Select(item => item.JogoId).Should().Equal(recente.JogoId, antigo.JogoId);
    }

    [Fact]
    public async Task SemItensProjetadosDeveDevolverListaVazia()
    {
        var usuarioId = Guid.NewGuid();
        Responder(usuarioId, []);

        IReadOnlyList<ItemBibliotecaResponse> resultado = await _useCase.ExecutarAsync(
            usuarioId,
            CancellationToken.None
        );

        resultado.Should().BeEmpty();
    }

    private void Responder(Guid usuarioId, IReadOnlyList<ItemBibliotecaProjetado> itens) =>
        _bibliotecaReadModel
            .Setup(m => m.ListarPorUsuarioAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(itens);

    private static ItemBibliotecaProjetado ItemDe(Guid usuarioId, DateTime adquiridoEm) =>
        new(usuarioId, Guid.NewGuid(), Guid.NewGuid(), "Hollow Knight", 19.90m, adquiridoEm);
}
