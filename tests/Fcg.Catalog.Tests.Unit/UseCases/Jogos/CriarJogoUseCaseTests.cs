using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Jogos;

public class CriarJogoUseCaseTests
{
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly CriarJogoUseCase _useCase;

    public CriarJogoUseCaseTests() =>
        _useCase = new CriarJogoUseCase(_jogoRepository.Object, _unitOfWork.Object);

    [Fact]
    public async Task DeveAdicionarOJogoEComitar()
    {
        var request = new CriarJogoRequest("Celeste", 75m, "Plataforma", "Maddy Makes Games");

        JogoResponse response = await _useCase.ExecutarAsync(request, CancellationToken.None);

        response.Titulo.Should().Be("Celeste");
        response.Preco.Should().Be(75m);
        response.Ativo.Should().BeTrue();
        _jogoRepository.Verify(
            r => r.AdicionarAsync(It.IsAny<Jogo>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
        _unitOfWork.Verify(u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
