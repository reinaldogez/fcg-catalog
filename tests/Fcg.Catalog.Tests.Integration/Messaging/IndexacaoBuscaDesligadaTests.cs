using System.Collections.Concurrent;
using Fcg.Catalog.Application.Events;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Messaging;

// A fixture compartilhada não tem índice de busca: o consumer de indexação existe mesmo assim, e
// a escrita cai no null object. Os hosted services do MassTransit são removidos na fixture, então
// o bus é iniciado sob demanda, como nos demais testes de mensageria.
public class IndexacaoBuscaDesligadaTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    private const string FilaDeErroDaIndexacao = "jogo-alterado.fcg-catalog_error";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ComBuscaDesligadaOEventoDeveSerConfirmadoSemIrParaAFilaDeErro()
    {
        Guid jogoExistente = await SemearJogoAsync();
        var jogoInexistente = Guid.NewGuid();
        uint errosAntes = await MensagensNaFilaDeErroAsync();

        IBusControl bus = Factory.Services.GetRequiredService<IBusControl>();
        ObservadorDeConsumo observador = new();
        using CancellationTokenSource cts = new(s_timeout);

        await bus.StartAsync(cts.Token);
        ConnectHandle conexao = bus.ConnectConsumeMessageObserver(observador);
        try
        {
            await bus.Publish(new JogoAlteradoEvent { JogoId = jogoExistente }, cts.Token);
            await bus.Publish(new JogoAlteradoEvent { JogoId = jogoInexistente }, cts.Token);

            (await AguardarAsync(() => observador.Concluidos.ContainsKey(jogoExistente)))
                .Should()
                .BeTrue("o evento de um jogo existente deve ser consumido até o fim");
            (await AguardarAsync(() => observador.Concluidos.ContainsKey(jogoInexistente)))
                .Should()
                .BeTrue("o evento de um jogo inexistente também é confirmado");
        }
        finally
        {
            conexao.Disconnect();
            await PararAsync(bus);
        }

        observador.Falhas.Should().BeEmpty("sem índice, a escrita é descartada sem erro");
        (await MensagensNaFilaDeErroAsync())
            .Should()
            .Be(errosAntes, "a busca desligada não é falha de consumo");
    }

    private async Task<Guid> SemearJogoAsync()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hollow Knight"), Preco.Criar(19.90m));

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Jogos.Add(jogo);
        await db.SaveChangesAsync();

        return jogo.Id;
    }

    private async Task<uint> MensagensNaFilaDeErroAsync()
    {
        ConnectionFactory fabrica = new() { Uri = new Uri(Factory.RabbitMqConnectionString) };
        await using IConnection conexao = await fabrica.CreateConnectionAsync();
        await using IChannel canal = await conexao.CreateChannelAsync();

        try
        {
            QueueDeclareOk fila = await canal.QueueDeclarePassiveAsync(FilaDeErroDaIndexacao);
            return fila.MessageCount;
        }
        catch (OperationInterruptedException)
        {
            // A fila de erro só passa a existir quando a primeira mensagem terminal chega nela.
            return 0;
        }
    }

    // O desligamento tem prazo próprio: parar com um token já cancelado deixa o consumidor
    // registrado no broker, e as mensagens dos testes seguintes iriam para um bus parado.
    private static async Task PararAsync(IBusControl bus)
    {
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StopAsync(cts.Token);
    }

    private static async Task<bool> AguardarAsync(Func<bool> condicao)
    {
        DateTime limite = DateTime.UtcNow + s_timeout;
        while (DateTime.UtcNow < limite)
        {
            if (condicao())
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    // Fim do consumo sem exceção é o que o bus confirma ao broker: é o sinal positivo de ACK,
    // em vez de inferir pela fila vazia, que também fica vazia com a mensagem ainda em voo.
    private sealed class ObservadorDeConsumo : IConsumeMessageObserver<JogoAlteradoEvent>
    {
        public ConcurrentDictionary<Guid, bool> Concluidos { get; } = new();

        public ConcurrentBag<Exception> Falhas { get; } = [];

        public Task PreConsume(ConsumeContext<JogoAlteradoEvent> context) => Task.CompletedTask;

        public Task PostConsume(ConsumeContext<JogoAlteradoEvent> context)
        {
            Concluidos[context.Message.JogoId] = true;
            return Task.CompletedTask;
        }

        public Task ConsumeFault(ConsumeContext<JogoAlteradoEvent> context, Exception exception)
        {
            Falhas.Add(exception);
            return Task.CompletedTask;
        }
    }
}
