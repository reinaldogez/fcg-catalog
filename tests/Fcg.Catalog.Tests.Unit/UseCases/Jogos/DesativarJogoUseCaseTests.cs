using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Jogos;

public class DesativarJogoUseCaseTests
{
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly DesativarJogoUseCase _useCase;

    public DesativarJogoUseCaseTests() =>
        _useCase = new DesativarJogoUseCase(_jogoRepository.Object, _unitOfWork.Object);

    [Fact]
    public async Task DeveDesativarChamarAtualizarEComitar()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hollow Knight"), Preco.Criar(50m));
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);

        bool resultado = await _useCase.ExecutarAsync(jogo.Id, CancellationToken.None);

        resultado.Should().BeTrue();
        jogo.Ativo.Should().BeFalse();
        _jogoRepository.Verify(r => r.Atualizar(jogo), Times.Once);
        _unitOfWork.Verify(u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeveRetornarFalsoESemComitarQuandoJogoNaoExiste()
    {
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Jogo?)null);

        bool resultado = await _useCase.ExecutarAsync(Guid.NewGuid(), CancellationToken.None);

        resultado.Should().BeFalse();
        _jogoRepository.Verify(r => r.Atualizar(It.IsAny<Jogo>()), Times.Never);
        _unitOfWork.Verify(
            u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
