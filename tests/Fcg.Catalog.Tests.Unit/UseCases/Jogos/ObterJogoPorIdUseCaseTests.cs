using Fcg.Catalog.Application.Abstractions;
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
    private readonly Mock<ICacheCatalogo> _cacheCatalogo = new();
    private readonly ObterJogoPorIdUseCase _useCase;

    public ObterJogoPorIdUseCaseTests() =>
        _useCase = new ObterJogoPorIdUseCase(_jogoRepository.Object, _cacheCatalogo.Object);

    [Fact]
    public async Task DeveMapearOJogoEncontrado()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Gris"), Preco.Criar(45m));
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);

        (JogoResponse? response, ResultadoDeCache cache) = await _useCase.ExecutarAsync(
            jogo.Id,
            CancellationToken.None
        );

        response.Should().NotBeNull();
        response!.Id.Should().Be(jogo.Id);
        response.Titulo.Should().Be("Gris");
        cache.Should().Be(ResultadoDeCache.Miss);

        _cacheCatalogo.Verify(
            c =>
                c.GravarDetalheAsync(
                    It.Is<JogoResponse>(j => j.Id == jogo.Id),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task DeveRetornarNuloQuandoNaoEncontrado()
    {
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Jogo?)null);

        (JogoResponse? response, ResultadoDeCache cache) = await _useCase.ExecutarAsync(
            Guid.NewGuid(),
            CancellationToken.None
        );

        response.Should().BeNull();
        cache.Should().Be(ResultadoDeCache.Miss);
    }

    [Fact]
    public async Task AusenciaNaoDeveSerGravadaNoCache()
    {
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Jogo?)null);

        await _useCase.ExecutarAsync(Guid.NewGuid(), CancellationToken.None);

        // Cachear ausência abriria poluição trivial por identificadores aleatórios, e a criação de
        // um jogo não teria como remover a chave de um id que ainda não existia.
        _cacheCatalogo.Verify(
            c => c.GravarDetalheAsync(It.IsAny<JogoResponse>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ComDetalheNoCacheNaoDeveConsultarORepositorio()
    {
        var cacheado = new JogoResponse(
            Guid.NewGuid(),
            "Vindo do cache",
            null,
            19.90m,
            null,
            null,
            true,
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)
        );
        _cacheCatalogo
            .Setup(c => c.ObterDetalheAsync(cacheado.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cacheado);

        (JogoResponse? response, ResultadoDeCache cache) = await _useCase.ExecutarAsync(
            cacheado.Id,
            CancellationToken.None
        );

        response.Should().Be(cacheado);
        cache.Should().Be(ResultadoDeCache.Hit);
        _jogoRepository.Verify(
            r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
