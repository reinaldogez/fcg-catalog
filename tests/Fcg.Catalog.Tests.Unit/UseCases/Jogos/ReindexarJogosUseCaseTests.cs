using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Jogos;

public class ReindexarJogosUseCaseTests
{
    private readonly Mock<IPreparacaoDoIndice> _preparacao = new();
    private readonly Mock<IIndiceDeJogos> _indice = new();
    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly ReindexarJogosUseCase _useCase;

    public ReindexarJogosUseCaseTests()
    {
        _useCase = new ReindexarJogosUseCase(
            _preparacao.Object,
            _indice.Object,
            _jogoRepository.Object,
            NullLogger<ReindexarJogosUseCase>.Instance
        );

        _indice
            .Setup(i =>
                i.GravarAsync(
                    It.IsAny<DocumentoJogo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(ResultadoEscritaIndice.Aplicada);
        _indice
            .Setup(i =>
                i.RemoverAsync(It.IsAny<Guid>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(ResultadoEscritaIndice.Aplicada);
    }

    [Fact]
    public async Task IndiceVazioDeveGravarOsAtivosERemoverOsInativosComVersao()
    {
        var ativo = Jogo.Criar(Titulo.Criar("Hollow Knight"), Preco.Criar(19.90m));
        var outroAtivo = Jogo.Criar(Titulo.Criar("Celeste"), Preco.Criar(45m));
        var inativo = Jogo.Criar(Titulo.Criar("Gris"), Preco.Criar(45m));
        inativo.Desativar();
        Fonte(ativo, outroAtivo, inativo);
        _preparacao.Setup(p => p.ContarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        ResumoReindexacao resumo = await _useCase.ExecutarAsync(somenteSeVazio: true);

        resumo.Should().Be(new ResumoReindexacao(Indexados: 2, Removidos: 1, Pulou: false));
        _indice.Verify(
            i =>
                i.GravarAsync(
                    DocumentoJogo.De(ativo),
                    VersaoDoJogo.De(ativo),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        _indice.Verify(
            i =>
                i.GravarAsync(
                    DocumentoJogo.De(outroAtivo),
                    VersaoDoJogo.De(outroAtivo),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        _indice.Verify(
            i =>
                i.RemoverAsync(inativo.Id, VersaoDoJogo.De(inativo), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    // O índice é garantido antes de a fonte ser lida: o que foi gravado antes entra pela leitura,
    // e o que vier depois encontra o índice criado e entra pelo consumer.
    [Fact]
    public async Task OIndiceDeveSerGarantidoAntesDaContagemEDaLeituraDaFonte()
    {
        List<string> ordem = [];
        _preparacao
            .Setup(p => p.GarantirAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("garantir"))
            .Returns(Task.CompletedTask);
        _preparacao
            .Setup(p => p.ContarAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("contar"))
            .ReturnsAsync(0);
        _jogoRepository
            .Setup(r =>
                r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
            )
            .Callback(() => ordem.Add("ler"))
            .ReturnsAsync([]);

        await _useCase.ExecutarAsync(somenteSeVazio: true);

        ordem.Should().Equal("garantir", "contar", "ler");
    }

    [Fact]
    public async Task IndiceJaPreenchidoComACondicaoDeVazioDevePularSemLerAFonte()
    {
        _preparacao.Setup(p => p.ContarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(22);

        ResumoReindexacao resumo = await _useCase.ExecutarAsync(somenteSeVazio: true);

        resumo.Should().Be(new ResumoReindexacao(0, 0, Pulou: true));
        _preparacao.Verify(p => p.GarantirAsync(It.IsAny<CancellationToken>()), Times.Once);
        _jogoRepository.Verify(
            r => r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        _indice.Verify(
            i =>
                i.GravarAsync(
                    It.IsAny<DocumentoJogo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    // Sem a condição, o índice preenchido é reescrito inteiro: reescrever o mesmo estado com a
    // mesma versão é inócuo.
    [Fact]
    public async Task ReconstrucaoTotalDeveReescreverMesmoComOIndicePreenchido()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hades"), Preco.Criar(90m));
        Fonte(jogo);
        _preparacao.Setup(p => p.ContarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(22);

        ResumoReindexacao resumo = await _useCase.ExecutarAsync(somenteSeVazio: false);

        resumo.Should().Be(new ResumoReindexacao(1, 0, Pulou: false));
        _preparacao.Verify(p => p.GarantirAsync(It.IsAny<CancellationToken>()), Times.Once);
        _preparacao.Verify(p => p.ContarAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DevePercorrerAFonteEmPaginasDe500AteAPrimeiraVazia()
    {
        List<Jogo> jogos =
        [
            .. Enumerable
                .Range(0, 1201)
                .Select(n => Jogo.Criar(Titulo.Criar($"Jogo {n}"), Preco.Criar(10m))),
        ];

        // Captura em lista em vez de um Verify por jogo: cada Verify varre todas as invocações, e
        // mil deles tornariam o teste quadrático.
        List<(int Pagina, int Tamanho)> paginasLidas = [];
        _jogoRepository
            .Setup(r =>
                r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
            )
            .Callback<int, int, CancellationToken>(
                (pagina, tamanho, _) => paginasLidas.Add((pagina, tamanho))
            )
            .ReturnsAsync(
                (int pagina, int tamanho, CancellationToken _) =>
                    jogos.Skip((pagina - 1) * tamanho).Take(tamanho).ToList()
            );
        List<(DocumentoJogo Documento, long Versao)> gravados = [];
        _indice
            .Setup(i =>
                i.GravarAsync(
                    It.IsAny<DocumentoJogo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<DocumentoJogo, long, CancellationToken>(
                (documento, versao, _) => gravados.Add((documento, versao))
            )
            .ReturnsAsync(ResultadoEscritaIndice.Aplicada);

        ResumoReindexacao resumo = await _useCase.ExecutarAsync(somenteSeVazio: false);

        resumo.Indexados.Should().Be(1201);
        paginasLidas.Should().Equal((1, 500), (2, 500), (3, 500), (4, 500));
        gravados.Should().Equal(jogos.Select(j => (DocumentoJogo.De(j), VersaoDoJogo.De(j))));
    }

    // O estado mais novo já gravado vence; o jogo continua contado como indexado, porque o
    // índice o tem.
    [Fact]
    public async Task EscritaObsoletaNaoDeveInterromperAReconstrucao()
    {
        var obsoleto = Jogo.Criar(Titulo.Criar("Limbo"), Preco.Criar(40m));
        var seguinte = Jogo.Criar(Titulo.Criar("Inside"), Preco.Criar(40m));
        Fonte(obsoleto, seguinte);
        _indice
            .Setup(i =>
                i.GravarAsync(
                    DocumentoJogo.De(obsoleto),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(ResultadoEscritaIndice.Obsoleta);

        ResumoReindexacao resumo = await _useCase.ExecutarAsync(somenteSeVazio: false);

        resumo.Should().Be(new ResumoReindexacao(2, 0, Pulou: false));
    }

    // O índice acabou de ser garantido: sumir no meio não pode terminar como reconstrução
    // completa.
    [Fact]
    public async Task IndiceQueSomeDuranteAReconstrucaoDeveFalhar()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Cuphead"), Preco.Criar(60m));
        Fonte(jogo);
        _indice
            .Setup(i =>
                i.GravarAsync(
                    It.IsAny<DocumentoJogo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(ResultadoEscritaIndice.IndiceAusente);

        Func<Task> reindexar = () => _useCase.ExecutarAsync(somenteSeVazio: false);

        await reindexar.Should().ThrowAsync<InvalidOperationException>();
    }

    // Dublê paginado sobre uma lista em memória: devolve a fatia da página pedida, e vazio depois
    // da última, como o repositório real.
    private void Fonte(params Jogo[] jogos) =>
        _jogoRepository
            .Setup(r =>
                r.ListarAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                (int pagina, int tamanho, CancellationToken _) =>
                    jogos.Skip((pagina - 1) * tamanho).Take(tamanho).ToList()
            );
}
