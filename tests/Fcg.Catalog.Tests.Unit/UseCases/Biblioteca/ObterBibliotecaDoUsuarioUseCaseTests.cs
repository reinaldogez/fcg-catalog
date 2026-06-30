using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Biblioteca;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Biblioteca;

public class ObterBibliotecaDoUsuarioUseCaseTests
{
    private readonly Mock<IItemBibliotecaRepository> _itemBibliotecaRepository = new();
    private readonly ObterBibliotecaDoUsuarioUseCase _useCase;

    public ObterBibliotecaDoUsuarioUseCaseTests() =>
        _useCase = new ObterBibliotecaDoUsuarioUseCase(_itemBibliotecaRepository.Object);

    [Fact]
    public async Task DeveMapearOsItensDoUsuario()
    {
        var usuarioId = Guid.NewGuid();
        IReadOnlyList<ItemBiblioteca> itens =
        [
            ItemBiblioteca.Criar(usuarioId, Guid.NewGuid()),
            ItemBiblioteca.Criar(usuarioId, Guid.NewGuid()),
        ];
        _itemBibliotecaRepository
            .Setup(r => r.ListarPorUsuarioAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(itens);

        IReadOnlyList<ItemBibliotecaResponse> resultado = await _useCase.ExecutarAsync(
            usuarioId,
            CancellationToken.None
        );

        resultado.Should().HaveCount(2);
        resultado.Should().OnlyContain(i => i.UsuarioId == usuarioId);
    }
}
