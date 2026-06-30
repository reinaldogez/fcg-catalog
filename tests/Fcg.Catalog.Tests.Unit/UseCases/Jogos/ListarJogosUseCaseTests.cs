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
    private readonly ListarJogosUseCase _useCase;

    public ListarJogosUseCaseTests() => _useCase = new ListarJogosUseCase(_jogoRepository.Object);

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

        IReadOnlyList<JogoResponse> resultado = await _useCase.ExecutarAsync(
            new ListarJogosRequest(),
            CancellationToken.None
        );

        resultado.Should().HaveCount(2);
        resultado.Select(j => j.Titulo).Should().ContainInOrder("Celeste", "Limbo");
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
    }
}
