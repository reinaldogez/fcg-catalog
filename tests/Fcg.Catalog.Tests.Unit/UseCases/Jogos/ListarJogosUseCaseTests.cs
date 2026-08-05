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

public class ListarJogosUseCaseTests
{
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly Mock<ICacheCatalogo> _cacheCatalogo = new();
    private readonly ListarJogosUseCase _useCase;

    public ListarJogosUseCaseTests() =>
        _useCase = new ListarJogosUseCase(_jogoRepository.Object, _cacheCatalogo.Object);

    [Fact]
    public async Task DeveMapearAListaDoRepositorio()
    {
        IReadOnlyList<Jogo> jogos =
        [
            Jogo.Criar(Titulo.Criar("Celeste"), Preco.Criar(75m)),
            Jogo.Criar(Titulo.Criar("Limbo"), Preco.Criar(40m)),
        ];
        _jogoRepository
            .Setup(r =>
                r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(jogos);

        (IReadOnlyList<JogoResponse> resultado, ResultadoDeCache cache) =
            await _useCase.ExecutarAsync(new ListarJogosRequest(), CancellationToken.None);

        resultado.Should().HaveCount(2);
        resultado.Select(j => j.Titulo).Should().ContainInOrder("Celeste", "Limbo");
        cache.Should().Be(ResultadoDeCache.Miss);
    }

    [Fact]
    public async Task DeveSanearPaginacaoInvalida()
    {
        _jogoRepository
            .Setup(r =>
                r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([]);

        await _useCase.ExecutarAsync(
            new ListarJogosRequest(Pagina: 0, TamanhoPagina: 5000),
            CancellationToken.None
        );

        // Página < 1 vira 1; tamanho acima do teto é limitado a 100.
        _jogoRepository.Verify(
            r => r.ListarAsync(1, 100, It.IsAny<CancellationToken>()),
            Times.Once
        );

        // O saneamento precede a consulta ao cache: a entrada é indexada pelo par já corrigido, e
        // não pelo que veio na requisição — do contrário `pagina=0` e `pagina=1` seriam duas cópias
        // do mesmo conteúdo, com invalidações independentes.
        _cacheCatalogo.Verify(
            c => c.ObterListagemAsync(1, 100, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task ComListagemNoCacheNaoDeveConsultarORepositorio()
    {
        IReadOnlyList<JogoResponse> cacheado = [Resposta("Vindo do cache")];
        _cacheCatalogo
            .Setup(c => c.ObterListagemAsync(1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cacheado);

        (IReadOnlyList<JogoResponse> resultado, ResultadoDeCache cache) =
            await _useCase.ExecutarAsync(new ListarJogosRequest(), CancellationToken.None);

        resultado.Should().ContainSingle().Which.Titulo.Should().Be("Vindo do cache");
        cache.Should().Be(ResultadoDeCache.Hit);
        _jogoRepository.Verify(
            r => r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ListaVaziaNoCacheDeveSerAcertoENaoAusencia()
    {
        _cacheCatalogo
            .Setup(c => c.ObterListagemAsync(1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        (IReadOnlyList<JogoResponse> resultado, ResultadoDeCache cache) =
            await _useCase.ExecutarAsync(new ListarJogosRequest(), CancellationToken.None);

        resultado.Should().BeEmpty();
        cache.Should().Be(ResultadoDeCache.Hit);
        _jogoRepository.Verify(
            r => r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task NaFaltaDeveGravarNoCacheOQueVeioDoRepositorio()
    {
        _jogoRepository
            .Setup(r =>
                r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([Jogo.Criar(Titulo.Criar("Gris"), Preco.Criar(45m))]);

        await _useCase.ExecutarAsync(new ListarJogosRequest(), CancellationToken.None);

        _cacheCatalogo.Verify(
            c =>
                c.GravarListagemAsync(
                    1,
                    20,
                    It.Is<IReadOnlyList<JogoResponse>>(l => l.Single().Titulo == "Gris"),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    private static JogoResponse Resposta(string titulo) =>
        new(
            Guid.NewGuid(),
            titulo,
            null,
            19.90m,
            null,
            null,
            true,
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)
        );
}
