using System.Reflection;
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

public class IndexarJogoUseCaseTests
{
    private static readonly DateTime s_alteradoEm = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IJogoRepository> _jogoRepository = new();
    private readonly Mock<IIndiceDeJogos> _indice = new(MockBehavior.Strict);
    private readonly IndexarJogoUseCase _useCase;

    public IndexarJogoUseCaseTests() =>
        _useCase = new IndexarJogoUseCase(
            _jogoRepository.Object,
            _indice.Object,
            NullLogger<IndexarJogoUseCase>.Instance
        );

    [Fact]
    public async Task JogoAtivoDeveSerGravadoComODocumentoEAVersaoDoEstadoRelido()
    {
        Jogo jogo = JogoAlteradoEm(
            Jogo.Criar(
                Titulo.Criar("Hollow Knight"),
                Preco.Criar(19.90m),
                "Metroidvania",
                "Team Cherry",
                new DateTime(2017, 2, 24, 0, 0, 0, DateTimeKind.Utc)
            ),
            s_alteradoEm
        );
        Relido(jogo);
        _indice
            .Setup(i =>
                i.GravarAsync(
                    DocumentoJogo.De(jogo),
                    VersaoDoJogo.De(jogo),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(ResultadoEscritaIndice.Aplicada);

        await _useCase.ExecutarAsync(jogo.Id);

        _indice.Verify(
            i =>
                i.GravarAsync(
                    DocumentoJogo.De(jogo),
                    VersaoDoJogo.De(jogo),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task JogoDesativadoDeveSerRemovidoDoIndiceComAVersaoDoEstadoRelido()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Gris"), Preco.Criar(45m));
        jogo.Desativar();
        JogoAlteradoEm(jogo, s_alteradoEm);
        Relido(jogo);
        _indice
            .Setup(i =>
                i.RemoverAsync(jogo.Id, VersaoDoJogo.De(jogo), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(ResultadoEscritaIndice.Aplicada);

        await _useCase.ExecutarAsync(jogo.Id);

        // Strict: gravar um jogo inativo seria chamada não configurada e quebraria o teste.
        _indice.Verify(
            i => i.RemoverAsync(jogo.Id, VersaoDoJogo.De(jogo), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task JogoInexistenteDeveSerRemovidoSemVersao()
    {
        var jogoId = Guid.NewGuid();
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Jogo?)null);
        _indice
            .Setup(i => i.RemoverAsync(jogoId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoEscritaIndice.Aplicada);

        Func<Task> indexar = () => _useCase.ExecutarAsync(jogoId);

        await indexar.Should().NotThrowAsync();
        _indice.Verify(
            i => i.RemoverAsync(jogoId, null, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    // O índice já guarda um estado mais novo: o descarte é o desfecho certo, não uma falha a
    // repetir.
    [Fact]
    public async Task EscritaObsoletaNaoDeveLancar()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Celeste"), Preco.Criar(45m));
        Relido(jogo);
        _indice
            .Setup(i =>
                i.GravarAsync(
                    It.IsAny<DocumentoJogo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(ResultadoEscritaIndice.Obsoleta);

        Func<Task> indexar = () => _useCase.ExecutarAsync(jogo.Id);

        await indexar.Should().NotThrowAsync();
    }

    // Quem cria o índice é a reconstrução, e ela traz o jogo junto: lançar só encheria a fila de
    // erro com uma mensagem que repetir não resolve.
    [Fact]
    public async Task IndiceAusenteNaoDeveLancar()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hades"), Preco.Criar(90m));
        Relido(jogo);
        _indice
            .Setup(i =>
                i.GravarAsync(
                    It.IsAny<DocumentoJogo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(ResultadoEscritaIndice.IndiceAusente);

        Func<Task> indexar = () => _useCase.ExecutarAsync(jogo.Id);

        await indexar.Should().NotThrowAsync();
    }

    // Falha do índice sobe intacta: é ela que aciona o retry do consumer e, esgotado, a fila de
    // erro.
    [Fact]
    public async Task FalhaDoIndiceDevePropagarParaORetryDoConsumer()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Limbo"), Preco.Criar(40m));
        Relido(jogo);
        InvalidOperationException falha = new("cluster fora do ar");
        _indice
            .Setup(i =>
                i.GravarAsync(
                    It.IsAny<DocumentoJogo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(falha);

        Func<Task> indexar = () => _useCase.ExecutarAsync(jogo.Id);

        (await indexar.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(falha);
    }

    private void Relido(Jogo jogo) =>
        _jogoRepository
            .Setup(r => r.ObterPorIdAsync(jogo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jogo);

    // O instante de alteração só muda por mutação do jogo; aqui ele é fixado direto, no lugar do
    // valor que o banco devolveria na releitura.
    private static Jogo JogoAlteradoEm(Jogo jogo, DateTime instante)
    {
        typeof(Jogo)
            .GetProperty(nameof(Jogo.AtualizadoEm), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(jogo, instante);

        return jogo;
    }
}
