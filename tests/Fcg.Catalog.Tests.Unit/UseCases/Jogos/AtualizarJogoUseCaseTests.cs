using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Jogos;

public class AtualizarJogoUseCaseTests
{
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AtualizarJogoUseCase _useCase;

    public AtualizarJogoUseCaseTests() =>
        _useCase = new AtualizarJogoUseCase(_jogoRepository.Object, _unitOfWork.Object);

    [Fact]
    public async Task DeveAtualizarEComitarQuandoJogoExiste()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Limbo"), Preco.Criar(40m));
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);
        var request = new AtualizarJogoRequest("Inside", 60m);

        JogoResponse? response = await _useCase.ExecutarAsync(
            jogo.Id,
            request,
            CancellationToken.None
        );

        response.Should().NotBeNull();
        response!.Titulo.Should().Be("Inside");
        response.Preco.Should().Be(60m);
        _jogoRepository.Verify(r => r.Atualizar(jogo), Times.Once);
        _unitOfWork.Verify(u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeveRetornarNuloESemComitarQuandoJogoNaoExiste()
    {
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Jogo?)null);

        JogoResponse? response = await _useCase.ExecutarAsync(
            Guid.NewGuid(),
            new AtualizarJogoRequest("Gris", 45m),
            CancellationToken.None
        );

        response.Should().BeNull();
        _jogoRepository.Verify(r => r.Atualizar(It.IsAny<Jogo>()), Times.Never);
        _unitOfWork.Verify(
            u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
