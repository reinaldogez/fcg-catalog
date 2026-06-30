using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Jogos;

public class ObterJogoPorIdUseCaseTests
{
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly ObterJogoPorIdUseCase _useCase;

    public ObterJogoPorIdUseCaseTests() =>
        _useCase = new ObterJogoPorIdUseCase(_jogoRepository.Object);

    [Fact]
    public async Task DeveMapearOJogoEncontrado()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Gris"), Preco.Criar(45m));
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);

        JogoResponse? response = await _useCase.ExecutarAsync(jogo.Id, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Id.Should().Be(jogo.Id);
        response.Titulo.Should().Be("Gris");
    }

    [Fact]
    public async Task DeveRetornarNuloQuandoNaoEncontrado()
    {
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Jogo?)null);

        JogoResponse? response = await _useCase.ExecutarAsync(
            Guid.NewGuid(),
            CancellationToken.None
        );

        response.Should().BeNull();
    }
}
