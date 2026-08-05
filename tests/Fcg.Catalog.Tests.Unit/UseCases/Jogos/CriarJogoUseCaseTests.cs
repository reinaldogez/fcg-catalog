using Fcg.Catalog.Application.Abstractions;
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
    private readonly Mock<ICacheCatalogo> _cacheCatalogo = new();
    private readonly CriarJogoUseCase _useCase;

    public CriarJogoUseCaseTests() =>
        _useCase = new CriarJogoUseCase(
            _jogoRepository.Object,
            _unitOfWork.Object,
            _cacheCatalogo.Object
        );

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

    [Fact]
    public async Task DeveInvalidarSomenteAListagemDepoisDoCommit()
    {
        // Grava a ordem real das chamadas: invalidar antes do commit abriria janela para a leitura
        // recachear a listagem sem o jogo novo.
        List<string> ordem = [];
        _unitOfWork
            .Setup(u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("commit"))
            .Returns(Task.CompletedTask);
        _cacheCatalogo
            .Setup(c => c.InvalidarListagemAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("invalidar"))
            .Returns(Task.CompletedTask);

        await _useCase.ExecutarAsync(new CriarJogoRequest("Limbo", 40m), CancellationToken.None);

        ordem.Should().Equal("commit", "invalidar");

        // Nada a remover no detalhe: o id acabou de nascer e nunca foi cacheado.
        _cacheCatalogo.Verify(
            c => c.InvalidarDetalheAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
