using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.UseCases.Biblioteca;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.ReadModel;

// Exercita o componente contra os dois armazenamentos reais, sem invocar o binário: o modo Job em
// si é estrutural — o que se prova aqui é a reconstrução, e a resolução da flag fica com o teste de
// composição-raiz.
public class ReprojecaoBibliotecaTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    private IBibliotecaReadModel Consulta =>
        Factory.Services.GetRequiredService<IBibliotecaReadModel>();

    private IProjecaoBiblioteca Projecao =>
        Factory.Services.GetRequiredService<IProjecaoBiblioteca>();

    [Fact]
    public async Task DeveProjetarTodosOsItensQuandoOModeloDeLeituraEstaVazio()
    {
        var usuarioId = Guid.NewGuid();
        Compra primeira = await CriarCompraAsync(usuarioId, "Jogo A", 10.00m);
        await CriarCompraAsync(usuarioId, "Jogo B", 20.00m);
        await CriarCompraAsync(usuarioId, "Jogo C", 30.00m);

        (ResumoReprojecaoBiblioteca resumo, _) = await ReprojetarAsync();

        resumo.Should().Be(new ResumoReprojecaoBiblioteca(3, 0));

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            usuarioId
        );

        itens.Should().HaveCount(3);
        itens.Select(item => item.NomeJogo).Should().BeEquivalentTo(["Jogo A", "Jogo B", "Jogo C"]);
        itens
            .Should()
            .ContainSingle(item =>
                item.JogoId == primeira.JogoId
                && item.PedidoId == primeira.PedidoId
                && item.Preco == 10.00m
            );
    }

    [Fact]
    public async Task DuasExecucoesSeguidasDevemDeixarOMesmoEstado()
    {
        var usuarioId = Guid.NewGuid();
        await CriarCompraAsync(usuarioId, "Jogo A", 10.00m);
        await CriarCompraAsync(usuarioId, "Jogo B", 20.00m);

        await ReprojetarAsync();
        IReadOnlyList<ItemBibliotecaProjetado> aposPrimeira = await Consulta.ListarPorUsuarioAsync(
            usuarioId
        );

        (ResumoReprojecaoBiblioteca resumo, _) = await ReprojetarAsync();
        IReadOnlyList<ItemBibliotecaProjetado> aposSegunda = await Consulta.ListarPorUsuarioAsync(
            usuarioId
        );

        resumo.Should().Be(new ResumoReprojecaoBiblioteca(2, 0));
        aposSegunda.Should().BeEquivalentTo(aposPrimeira);
    }

    [Fact]
    public async Task ItemDeJogoDesativadoDeveSerReprojetado()
    {
        var usuarioId = Guid.NewGuid();
        Compra compra = await CriarCompraAsync(
            usuarioId,
            "Jogo Fora de Catálogo",
            15.00m,
            desativarJogo: true
        );

        (ResumoReprojecaoBiblioteca resumo, _) = await ReprojetarAsync();

        resumo.Should().Be(new ResumoReprojecaoBiblioteca(1, 0));

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            usuarioId
        );

        itens.Should().ContainSingle().Which.JogoId.Should().Be(compra.JogoId);
    }

    [Fact]
    public async Task ItemSemPedidoAprovadoDeveSerPuladoLogadoEContabilizado()
    {
        var usuarioId = Guid.NewGuid();
        Compra completa = await CriarCompraAsync(usuarioId, "Jogo Comprado", 10.00m);
        Compra orfa = await CriarCompraAsync(
            usuarioId,
            "Jogo Sem Pedido Aprovado",
            20.00m,
            aprovarPedido: false
        );

        (ResumoReprojecaoBiblioteca resumo, IReadOnlyList<string> avisos) = await ReprojetarAsync();

        resumo.Should().Be(new ResumoReprojecaoBiblioteca(1, 1));
        avisos.Should().ContainSingle().Which.Should().Contain(orfa.JogoId.ToString());

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            usuarioId
        );

        itens.Should().ContainSingle().Which.JogoId.Should().Be(completa.JogoId);
    }

    [Fact]
    public async Task ReprojecaoSobreModeloPopuladoDeveManterChavesPedidoEPreco()
    {
        var usuarioId = Guid.NewGuid();
        Compra compra = await CriarCompraAsync(usuarioId, "Título Atual", 49.90m);

        // Estado que o caminho de evento gravaria: o título vigente no instante da compra e o
        // carimbo de processamento do pagamento.
        ItemBibliotecaProjetado peloEvento = new(
            compra.UsuarioId,
            compra.JogoId,
            compra.PedidoId,
            "Título no Instante da Compra",
            compra.Preco,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        await Projecao.ProjetarAsync(peloEvento);

        await ReprojetarAsync();

        ItemBibliotecaProjetado depois = (await Consulta.ListarPorUsuarioAsync(usuarioId))
            .Should()
            .ContainSingle()
            .Which;

        depois.UsuarioId.Should().Be(peloEvento.UsuarioId);
        depois.JogoId.Should().Be(peloEvento.JogoId);
        depois.PedidoId.Should().Be(peloEvento.PedidoId);
        depois.Preco.Should().Be(peloEvento.Preco);

        // As duas divergências são o comportamento correto, não defeito: o nome vem do título atual
        // do catálogo e a data vem do carimbo de crédito, não do instante do pagamento.
        depois.NomeJogo.Should().Be("Título Atual");
        depois.AdquiridoEm.Should().NotBe(peloEvento.AdquiridoEm);
        depois.AdquiridoEm.Should().BeCloseTo(compra.AdicionadoEm, TimeSpan.FromMilliseconds(1));
    }

    // Substitui só o logger do sujeito: a borda do item órfão exige que o aviso apareça, e o
    // resto do grafo continua sendo o de produção.
    private async Task<(
        ResumoReprojecaoBiblioteca Resumo,
        IReadOnlyList<string> Avisos
    )> ReprojetarAsync()
    {
        await using AsyncServiceScope escopo = Factory.Services.CreateAsyncScope();

        LoggerCapturador<ReprojetarBibliotecaUseCase> logger = new();

        ReprojetarBibliotecaUseCase reprojetor = new(
            escopo.ServiceProvider.GetRequiredService<IFonteReprojecaoBiblioteca>(),
            escopo.ServiceProvider.GetRequiredService<IProjecaoBiblioteca>(),
            logger
        );

        ResumoReprojecaoBiblioteca resumo = await reprojetor.ExecutarAsync();

        return (resumo, logger.Avisos);
    }

    private async Task<Compra> CriarCompraAsync(
        Guid usuarioId,
        string titulo,
        decimal preco,
        bool aprovarPedido = true,
        bool desativarJogo = false
    )
    {
        await using AsyncServiceScope escopo = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = escopo.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var jogo = Jogo.Criar(Titulo.Criar(titulo), Preco.Criar(preco));

        if (desativarJogo)
            jogo.Desativar();

        var pedido = Pedido.Criar(usuarioId, jogo.Id, Preco.Criar(preco));

        if (aprovarPedido)
            pedido.Aprovar();

        var item = ItemBiblioteca.Criar(usuarioId, jogo.Id);

        db.Jogos.Add(jogo);
        db.Pedidos.Add(pedido);
        db.ItensBiblioteca.Add(item);
        await db.SalvarAlteracoesAsync();

        return new Compra(usuarioId, jogo.Id, pedido.Id, preco, item.AdicionadoEm);
    }

    private sealed record Compra(
        Guid UsuarioId,
        Guid JogoId,
        Guid PedidoId,
        decimal Preco,
        DateTime AdicionadoEm
    );

    private sealed class LoggerCapturador<T> : ILogger<T>
    {
        public List<string> Avisos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Avisos.Add(formatter(state, exception));
        }
    }
}
