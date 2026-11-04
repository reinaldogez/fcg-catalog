using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.Events;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using MassTransit;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Jogos;

public class AtualizarJogoUseCaseTests
{
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICacheCatalogo> _cacheCatalogo = new();
    private readonly Mock<IPublishEndpoint> _publishEndpoint = new();
    private readonly AtualizarJogoUseCase _useCase;

    public AtualizarJogoUseCaseTests() =>
        _useCase = new AtualizarJogoUseCase(
            _jogoRepository.Object,
            _unitOfWork.Object,
            _cacheCatalogo.Object,
            _publishEndpoint.Object
        );

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

        // Sem escrita não há o que invalidar, e subir a versão à toa descartaria toda página viva.
        _cacheCatalogo.Verify(
            c => c.InvalidarListagemAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
        _cacheCatalogo.Verify(
            c => c.InvalidarDetalheAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );

        // Sem jogo não há o que indexar: o evento só nasce junto de uma escrita.
        _publishEndpoint.Verify(
            p => p.Publish(It.IsAny<JogoAlteradoEvent>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task DeveInvalidarListagemEDetalheDepoisDoCommit()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Celeste"), Preco.Criar(75m));
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);

        // O jogo alterado aparece no detalhe e dentro das páginas: as duas invalidações vêm depois
        // do commit, e deixar uma de fora serviria o valor antigo por um dos dois caminhos.
        List<string> ordem = [];
        _unitOfWork
            .Setup(u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("commit"))
            .Returns(Task.CompletedTask);
        _cacheCatalogo
            .Setup(c => c.InvalidarListagemAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("listagem"))
            .Returns(Task.CompletedTask);
        _cacheCatalogo
            .Setup(c => c.InvalidarDetalheAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("detalhe"))
            .Returns(Task.CompletedTask);

        await _useCase.ExecutarAsync(
            jogo.Id,
            new AtualizarJogoRequest("Celeste GOTY", 29.90m),
            CancellationToken.None
        );

        ordem.Should().Equal("commit", "listagem", "detalhe");
    }

    [Fact]
    public async Task DevePublicarJogoAlteradoUmaVezAntesDoCommit()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Inside"), Preco.Criar(60m));
        Guid jogoId = jogo.Id;
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);

        // Grava a ordem real das chamadas: publicar antes do commit é o que põe a linha do outbox
        // na mesma transação do jogo.
        List<string> ordem = [];
        List<JogoAlteradoEvent> publicados = [];
        _publishEndpoint
            .Setup(p => p.Publish(It.IsAny<JogoAlteradoEvent>(), It.IsAny<CancellationToken>()))
            .Callback<JogoAlteradoEvent, CancellationToken>(
                (evento, _) =>
                {
                    ordem.Add("publish");
                    publicados.Add(evento);
                }
            )
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("commit"))
            .Returns(Task.CompletedTask);

        await _useCase.ExecutarAsync(
            jogo.Id,
            new AtualizarJogoRequest("Inside", 60m),
            CancellationToken.None
        );

        ordem.Should().Equal("publish", "commit");
        publicados.Should().ContainSingle().Which.JogoId.Should().Be(jogoId);
    }
}
