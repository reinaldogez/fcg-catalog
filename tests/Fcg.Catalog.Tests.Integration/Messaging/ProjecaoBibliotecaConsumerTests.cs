using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using Fcg.Contracts.Enums;
using Fcg.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Messaging;

// Exercita a fila de projeção pelo consumer real contra o RabbitMQ do Testcontainer, com o read
// model no DynamoDB local. Os hosted services do MassTransit são removidos na fixture, então o bus
// é iniciado sob demanda, como nos demais testes de mensageria.
public class ProjecaoBibliotecaConsumerTests(CatalogApiFactory factory)
    : IntegrationTestBase(factory)
{
    private const string FilaDeProjecao = "payment-processed.fcg-catalog-projections";
    private const string FilaDeErroDaProjecao = FilaDeProjecao + "_error";

    // Endereço sem ninguém escutando: recusa de conexão imediata, sem derrubar o container que os
    // outros testes da coleção compartilham.
    private const string EnderecoInalcancavel = "http://127.0.0.1:1";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset s_instante = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EventoAprovadoDeveCreditarNoPostgresEProjetarNoReadModel()
    {
        (Guid pedidoId, Guid usuarioId, Guid jogoId) = await SemearPedidoPendenteAsync();

        await ComOBusAsync(
            async (bus, ct) =>
            {
                await bus.Publish(
                    EventoAprovado(
                        pedidoId,
                        usuarioId,
                        jogoId,
                        "Hollow Knight",
                        19.90m,
                        s_instante
                    ),
                    ctx => ctx.MessageId = Guid.NewGuid(),
                    ct
                );

                (await AguardarAsync(async () => await ItensDoParAsync(usuarioId, jogoId) == 1))
                    .Should()
                    .BeTrue("o crédito na fonte da verdade segue pelo consumer que já existia");
                (
                    await AguardarAsync(async () =>
                        (await ItensProjetadosAsync(usuarioId)).Count == 1
                    )
                )
                    .Should()
                    .BeTrue("a projeção segue por fila própria, independente do crédito");
            }
        );

        (await ItensProjetadosAsync(usuarioId))
            .Single()
            .Should()
            .Be(
                new ItemBibliotecaProjetado(
                    usuarioId,
                    jogoId,
                    pedidoId,
                    "Hollow Knight",
                    19.90m,
                    s_instante.UtcDateTime
                )
            );
    }

    [Fact]
    public async Task EventoRejeitadoDeveConfirmarSemProjetar()
    {
        (Guid pedidoId, Guid usuarioId, Guid jogoId) = await SemearPedidoPendenteAsync();
        uint errosAntes = await MensagensNaFilaDeErroAsync();

        await ComOBusAsync(
            async (bus, ct) =>
            {
                await bus.Publish(
                    new PaymentProcessedEvent
                    {
                        OccurredAt = s_instante,
                        OrderId = pedidoId,
                        UserId = usuarioId,
                        GameId = jogoId,
                        GameName = "Hollow Knight",
                        Price = 19.90m,
                        Status = PaymentStatus.Rejected,
                        RejectionReason = "Saldo insuficiente",
                    },
                    ctx => ctx.MessageId = Guid.NewGuid(),
                    ct
                );

                // O desfecho observável do ramo rejeitado é o pedido recusado; a partir daí a
                // janela sustenta que a projeção não escreveu nada.
                (await AguardarAsync(async () => await StatusPedidoAsync(pedidoId) == 2))
                    .Should()
                    .BeTrue("o evento rejeitado deve marcar o pedido como Rejeitado");
                (await ManterAsync(async () => (await ItensProjetadosAsync(usuarioId)).Count == 0))
                    .Should()
                    .BeTrue("pagamento recusado não credita biblioteca");
            }
        );

        (await MensagensNaFilaDeErroAsync())
            .Should()
            .Be(errosAntes, "o ramo rejeitado é confirmado, não é falha");
    }

    [Fact]
    public async Task ProjetarOMesmoEventoDuasVezesNaoDuplicaItem()
    {
        (Guid pedidoId, Guid usuarioId, Guid jogoId) = await SemearPedidoPendenteAsync();
        PaymentProcessedEvent evento = EventoAprovado(
            pedidoId,
            usuarioId,
            jogoId,
            "Hollow Knight",
            19.90m,
            s_instante
        );

        await ComOBusAsync(
            async (bus, ct) =>
            {
                // MessageId distinto em cada entrega: sem Inbox nesta fila, as duas chegam ao
                // consumer de verdade. O que se prova aqui é a idempotência da escrita, não a
                // deduplicação.
                await bus.Publish(evento, ctx => ctx.MessageId = Guid.NewGuid(), ct);
                (
                    await AguardarAsync(async () =>
                        (await ItensProjetadosAsync(usuarioId)).Count == 1
                    )
                )
                    .Should()
                    .BeTrue("a primeira entrega deve projetar o item");

                await bus.Publish(evento, ctx => ctx.MessageId = Guid.NewGuid(), ct);
                (await ManterAsync(async () => (await ItensProjetadosAsync(usuarioId)).Count == 1))
                    .Should()
                    .BeTrue("a segunda entrega reescreve o mesmo item, não cria um segundo");
            }
        );

        (await ItensProjetadosAsync(usuarioId))
            .Single()
            .Should()
            .Be(
                new ItemBibliotecaProjetado(
                    usuarioId,
                    jogoId,
                    pedidoId,
                    "Hollow Knight",
                    19.90m,
                    s_instante.UtcDateTime
                )
            );
    }

    [Fact]
    public async Task ProjetarEventosForaDeOrdemDeveProduzirOMesmoEstadoFinal()
    {
        var usuarioId = Guid.NewGuid();
        (Guid pedidoAntigo, Guid jogoAntigo) = await SemearPedidoPendenteDoUsuarioAsync(usuarioId);
        (Guid pedidoRecente, Guid jogoRecente) = await SemearPedidoPendenteDoUsuarioAsync(
            usuarioId
        );

        DateTimeOffset instanteAntigo = s_instante;
        DateTimeOffset instanteRecente = s_instante.AddMinutes(30);

        ItemBibliotecaProjetado[] esperados =
        [
            new(usuarioId, jogoAntigo, pedidoAntigo, "Celeste", 45.00m, instanteAntigo.UtcDateTime),
            new(
                usuarioId,
                jogoRecente,
                pedidoRecente,
                "Hades",
                60.00m,
                instanteRecente.UtcDateTime
            ),
        ];

        await ComOBusAsync(
            async (bus, ct) =>
            {
                // A aquisição mais recente chega primeiro. Cada item tem chave própria, sem
                // agregação e sem contador, então a ordem de chegada não participa do estado final.
                await bus.Publish(
                    EventoAprovado(
                        pedidoRecente,
                        usuarioId,
                        jogoRecente,
                        "Hades",
                        60.00m,
                        instanteRecente
                    ),
                    ctx => ctx.MessageId = Guid.NewGuid(),
                    ct
                );
                await bus.Publish(
                    EventoAprovado(
                        pedidoAntigo,
                        usuarioId,
                        jogoAntigo,
                        "Celeste",
                        45.00m,
                        instanteAntigo
                    ),
                    ctx => ctx.MessageId = Guid.NewGuid(),
                    ct
                );

                (
                    await AguardarAsync(async () =>
                        (await ItensProjetadosAsync(usuarioId)).Count == 2
                    )
                )
                    .Should()
                    .BeTrue("os dois eventos aprovados devem projetar dois itens");
            }
        );

        (await ItensProjetadosAsync(usuarioId)).Should().BeEquivalentTo(esperados);
    }

    [Fact]
    public async Task EndpointDeProjecaoNaoDeclaraInbox()
    {
        (Guid pedidoId, Guid usuarioId, Guid jogoId) = await SemearPedidoPendenteAsync();
        var messageId = Guid.NewGuid();

        await ComOBusAsync(
            async (bus, ct) =>
            {
                await bus.Publish(
                    EventoAprovado(
                        pedidoId,
                        usuarioId,
                        jogoId,
                        "Hollow Knight",
                        19.90m,
                        s_instante
                    ),
                    ctx => ctx.MessageId = messageId,
                    ct
                );

                (
                    await AguardarAsync(async () =>
                        (await ItensProjetadosAsync(usuarioId)).Count == 1
                    )
                )
                    .Should()
                    .BeTrue(
                        "a projeção precisa ter rodado para a contagem do Inbox significar algo"
                    );

                // O Inbox grava uma linha por par mensagem-consumidor. Os dois endpoints recebem a
                // mesma mensagem; uma segunda linha só apareceria se o endpoint de projeção também
                // declarasse o Inbox — que é justamente o que ele não faz.
                (await ManterAsync(async () => await LinhasDeInboxAsync(messageId) == 1))
                    .Should()
                    .BeTrue("só o endpoint de crédito declara o Inbox");
            }
        );
    }

    [Fact]
    public async Task ComReadModelInalcancavelAMensagemVaiParaAFilaDeErroEOCreditoPermanece()
    {
        (Guid pedidoId, Guid usuarioId, Guid jogoId) = await SemearPedidoPendenteAsync();
        await PurgarFilaDeErroAsync();

        // Host derivado com o read model apontado para um endereço morto: o consumer de crédito
        // segue íntegro, e só o caminho da projeção falha.
        using WebApplicationFactory<Program> hostSemReadModel = Factory.WithWebHostBuilder(
            builder =>
            {
                builder.UseSetting("DynamoDb:ServiceUrl", EnderecoInalcancavel);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAmazonDynamoDB>();
                    services.AddSingleton<IAmazonDynamoDB>(_ => ClienteInalcancavel());
                });
            }
        );

        IBusControl bus = hostSemReadModel.Services.GetRequiredService<IBusControl>();
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StartAsync(cts.Token);
        try
        {
            await bus.Publish(
                EventoAprovado(pedidoId, usuarioId, jogoId, "Hollow Knight", 19.90m, s_instante),
                ctx => ctx.MessageId = Guid.NewGuid(),
                cts.Token
            );

            (await AguardarAsync(async () => await MensagensNaFilaDeErroAsync() == 1))
                .Should()
                .BeTrue("esgotadas as tentativas, a mensagem vai para a fila de erro do broker");
            (await ItensDoParAsync(usuarioId, jogoId))
                .Should()
                .Be(1, "o crédito na fonte da verdade não depende da projeção");
        }
        finally
        {
            await PararAsync(bus);
            await PurgarFilaDeErroAsync();
        }
    }

    // Aponta para o mesmo endereço morto do host, mas sem a cadeia de retry do SDK: com a política
    // padrão uma única tentativa segura mais de dez segundos, e as quatro execuções do consumer não
    // caberiam no tempo do teste. O que se exercita continua sendo o read model inalcançável.
    private static IAmazonDynamoDB ClienteInalcancavel() =>
        new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig
            {
                ServiceURL = EnderecoInalcancavel,
                AuthenticationRegion = "us-east-1",
                MaxErrorRetry = 0,
                Timeout = TimeSpan.FromSeconds(2),
            }
        );

    private static PaymentProcessedEvent EventoAprovado(
        Guid pedidoId,
        Guid usuarioId,
        Guid jogoId,
        string nomeJogo,
        decimal preco,
        DateTimeOffset ocorridoEm
    ) =>
        new()
        {
            OccurredAt = ocorridoEm,
            OrderId = pedidoId,
            UserId = usuarioId,
            GameId = jogoId,
            GameName = nomeJogo,
            Price = preco,
            Status = PaymentStatus.Approved,
        };

    // O bus da fixture não sobe sozinho (hosted services removidos): cada teste o liga, roda o
    // corpo e o desliga.
    private async Task ComOBusAsync(Func<IBusControl, CancellationToken, Task> corpo)
    {
        IBusControl bus = Factory.Services.GetRequiredService<IBusControl>();
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StartAsync(cts.Token);
        try
        {
            await corpo(bus, cts.Token);
        }
        finally
        {
            await PararAsync(bus);
        }
    }

    // O desligamento tem prazo próprio: parar com o token do corpo do teste, que pode já estar
    // cancelado, deixa o consumidor registrado no broker e as mensagens dos testes seguintes
    // passam a ser distribuídas para um bus que não processa mais nada.
    private static async Task PararAsync(IBusControl bus)
    {
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StopAsync(cts.Token);
    }

    private Task<IReadOnlyList<ItemBibliotecaProjetado>> ItensProjetadosAsync(Guid usuarioId) =>
        Factory
            .Services.GetRequiredService<IBibliotecaReadModel>()
            .ListarPorUsuarioAsync(usuarioId);

    private async Task<(Guid PedidoId, Guid UsuarioId, Guid JogoId)> SemearPedidoPendenteAsync()
    {
        var usuarioId = Guid.NewGuid();
        (Guid pedidoId, Guid jogoId) = await SemearPedidoPendenteDoUsuarioAsync(usuarioId);
        return (pedidoId, usuarioId, jogoId);
    }

    // Semeia direto no banco um Pedido pendente — dispensa montar jogo e passar pelo endpoint só
    // para ter um alvo do fechamento da saga.
    private async Task<(Guid PedidoId, Guid JogoId)> SemearPedidoPendenteDoUsuarioAsync(
        Guid usuarioId
    )
    {
        var jogoId = Guid.NewGuid();
        var pedido = Pedido.Criar(usuarioId, jogoId, Preco.Criar(120m));

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        return (pedido.Id, jogoId);
    }

    private async Task<int> StatusPedidoAsync(Guid pedidoId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db
            .Database.SqlQueryRaw<int>(
                "SELECT status AS \"Value\" FROM pedidos WHERE id = {0}",
                pedidoId
            )
            .SingleAsync();
    }

    private async Task<int> ItensDoParAsync(Guid usuarioId, Guid jogoId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db
            .Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM itens_biblioteca "
                    + "WHERE usuario_id = {0} AND jogo_id = {1}",
                usuarioId,
                jogoId
            )
            .SingleAsync();
    }

    private async Task<int> LinhasDeInboxAsync(Guid messageId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db
            .Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM inbox_state WHERE message_id = {0}",
                messageId
            )
            .SingleAsync();
    }

    private async Task<uint> MensagensNaFilaDeErroAsync()
    {
        ConnectionFactory fabrica = new() { Uri = new Uri(Factory.RabbitMqConnectionString) };
        await using IConnection conexao = await fabrica.CreateConnectionAsync();
        await using IChannel canal = await conexao.CreateChannelAsync();

        try
        {
            QueueDeclareOk fila = await canal.QueueDeclarePassiveAsync(FilaDeErroDaProjecao);
            return fila.MessageCount;
        }
        catch (OperationInterruptedException)
        {
            // A fila de erro só passa a existir quando a primeira mensagem terminal chega nela.
            return 0;
        }
    }

    // As filas do broker sobrevivem ao reset de banco entre testes: sem a purga, uma mensagem
    // terminal de um teste contaria na asserção de outro.
    private async Task PurgarFilaDeErroAsync()
    {
        ConnectionFactory fabrica = new() { Uri = new Uri(Factory.RabbitMqConnectionString) };
        await using IConnection conexao = await fabrica.CreateConnectionAsync();
        await using IChannel canal = await conexao.CreateChannelAsync();

        try
        {
            await canal.QueuePurgeAsync(FilaDeErroDaProjecao);
        }
        catch (OperationInterruptedException) { }
    }

    // Poll até a condição virar verdadeira (o consumo é assíncrono) ou o timeout estourar.
    private static async Task<bool> AguardarAsync(Func<Task<bool>> condicao)
    {
        DateTime limite = DateTime.UtcNow + s_timeout;
        while (DateTime.UtcNow < limite)
        {
            if (await condicao())
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    // Para provar ausência de efeito, exige-se que a condição permaneça verdadeira durante uma
    // janela — num instante só, ela poderia ser apenas o consumo ainda não ter acontecido.
    private static async Task<bool> ManterAsync(Func<Task<bool>> condicao)
    {
        DateTime limite = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < limite)
        {
            if (!await condicao())
                return false;
            await Task.Delay(200);
        }
        return true;
    }
}
