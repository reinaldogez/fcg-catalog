using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Jogos;

public class BuscarJogosUseCaseTests
{
    private readonly Mock<IBuscaDeJogos> _buscaDeJogos = new();
    private readonly BuscarJogosUseCase _useCase;

    public BuscarJogosUseCaseTests()
    {
        _buscaDeJogos
            .Setup(b =>
                b.BuscarAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([]);

        _useCase = new BuscarJogosUseCase(_buscaDeJogos.Object);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TermoAusenteOuEmBrancoDeveSerRecusadoSemConsultarOIndice(string? termo)
    {
        Func<Task> acao = () => _useCase.ExecutarAsync(new BuscarJogosRequest(termo));

        await acao.Should().ThrowAsync<DomainException>();
        NaoDeveTerConsultadoOIndice();
    }

    [Fact]
    public async Task TermoComMaisDe200CaracteresDepoisDoTrimDeveSerRecusado()
    {
        Func<Task> acao = () =>
            _useCase.ExecutarAsync(new BuscarJogosRequest(new string('a', 201)));

        await acao.Should().ThrowAsync<DomainException>();
        NaoDeveTerConsultadoOIndice();
    }

    [Fact]
    public async Task TermoDe200CaracteresComEspacosEmVoltaDeveSerAceitoJaAparado()
    {
        string termo = new('a', 200);

        await _useCase.ExecutarAsync(new BuscarJogosRequest($"  {termo}  "));

        _buscaDeJogos.Verify(
            b => b.BuscarAsync(termo, 0, 20, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task PaginacaoDeveSerSaneadaComoNaListagem()
    {
        await _useCase.ExecutarAsync(new BuscarJogosRequest("witcher", 0, 5000));

        _buscaDeJogos.Verify(
            b => b.BuscarAsync("witcher", 0, 100, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task TamanhoDePaginaAbaixoDeUmDeveVirarUm()
    {
        await _useCase.ExecutarAsync(new BuscarJogosRequest("witcher", 3, 0));

        _buscaDeJogos.Verify(
            b => b.BuscarAsync("witcher", 2, 1, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task UltimaPaginaDentroDaJanelaDeveChegarAoIndiceComODeslocamentoCerto()
    {
        await _useCase.ExecutarAsync(new BuscarJogosRequest("witcher", 100, 100));

        _buscaDeJogos.Verify(
            b => b.BuscarAsync("witcher", 9900, 100, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Theory]
    [InlineData(101, 100)]
    [InlineData(10_001, 1)]
    [InlineData(int.MaxValue, 100)]
    public async Task JanelaAcimaDe10000DeveSerRecusadaSemConsultarOIndice(
        int pagina,
        int tamanhoPagina
    )
    {
        Func<Task> acao = () =>
            _useCase.ExecutarAsync(new BuscarJogosRequest("witcher", pagina, tamanhoPagina));

        await acao.Should().ThrowAsync<DomainException>();
        NaoDeveTerConsultadoOIndice();
    }

    // A janela é a dos valores saneados: tamanho acima do teto é ajustado para 100, e a página 11
    // com 100 itens fica dentro do limite.
    [Fact]
    public async Task JanelaDeveSerMedidaDepoisDoSaneamento()
    {
        await _useCase.ExecutarAsync(new BuscarJogosRequest("witcher", 11, 1000));

        _buscaDeJogos.Verify(
            b => b.BuscarAsync("witcher", 1000, 100, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task DeveDevolverOResultadoDoIndiceSemAlterar()
    {
        ResultadoBuscaJogoResponse[] resultado =
        [
            new(Guid.NewGuid(), "The Witcher 3: Wild Hunt", null, 79.90m, null, null, 4.2),
            new(Guid.NewGuid(), "Elden Ring", null, 249.90m, null, null, 1.1),
        ];
        _buscaDeJogos
            .Setup(b => b.BuscarAsync("witcer", 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultado);

        IReadOnlyList<ResultadoBuscaJogoResponse> resposta = await _useCase.ExecutarAsync(
            new BuscarJogosRequest("witcer")
        );

        resposta.Should().Equal(resultado);
    }

    [Fact]
    public async Task IndisponibilidadeDaBuscaDevePropagar()
    {
        _buscaDeJogos
            .Setup(b =>
                b.BuscarAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new BuscaIndisponivelException("fora do ar"));

        Func<Task> acao = () => _useCase.ExecutarAsync(new BuscarJogosRequest("witcher"));

        await acao.Should().ThrowAsync<BuscaIndisponivelException>();
    }

    private void NaoDeveTerConsultadoOIndice() =>
        _buscaDeJogos.Verify(
            b =>
                b.BuscarAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
}
