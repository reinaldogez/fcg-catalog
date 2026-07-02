using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using Fcg.Contracts.Enums;
using Fcg.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Messaging;

// Fecha a saga ponta a ponta pelo consumer real contra o RabbitMQ do Testcontainer. Os hosted
// services do MassTransit são removidos na fixture, então o bus é iniciado sob demanda (como em
// TopologiaBusTests). O commit das escritas do consumer é do harness do Inbox — se o endpoint não
// engatar o UseEntityFrameworkOutbox, nada comitaria e os polls estourariam o timeout.
public class FechamentoSagaConsumerTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RedeliveryDoMesmoAprovadoNaoDuplicaItemBiblioteca()
    {
        (Guid pedidoId, Guid usuarioId, Guid jogoId) = await SemearPedidoPendenteAsync();
        var messageId = Guid.NewGuid();

        IBusControl bus = Factory.Services.GetRequiredService<IBusControl>();
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StartAsync(cts.Token);
        try
        {
            // Primeira entrega: fecha o pedido e materializa a biblioteca (commit do harness).
            await bus.Publish(
                EventoAprovado(pedidoId, usuarioId, jogoId),
                ctx => ctx.MessageId = messageId,
                cts.Token
            );
            (await AguardarAsync(async () => await StatusPedidoAsync(pedidoId) == 1))
                .Should()
                .BeTrue("a primeira entrega aprovada deve fechar o pedido");

            // Redelivery da MESMA mensagem (mesmo MessageId): o Inbox a absorve; e mesmo se
            // reprocessasse, o pedido já-terminal seria no-op. Nenhuma segunda linha de biblioteca.
            await bus.Publish(
                EventoAprovado(pedidoId, usuarioId, jogoId),
                ctx => ctx.MessageId = messageId,
                cts.Token
            );
            (await ManterAsync(async () => await ItensDoParAsync(usuarioId, jogoId) == 1))
                .Should()
                .BeTrue("a reentrega não pode duplicar o ItemBiblioteca");

            (await StatusPedidoAsync(pedidoId)).Should().Be(1);
            (await ItensDoParAsync(usuarioId, jogoId)).Should().Be(1);
        }
        finally
        {
            await bus.StopAsync(cts.Token);
        }
    }

    [Fact]
    public async Task RejeitadoGravaMotivoENaoCriaItemBiblioteca()
    {
        (Guid pedidoId, Guid usuarioId, Guid jogoId) = await SemearPedidoPendenteAsync();

        IBusControl bus = Factory.Services.GetRequiredService<IBusControl>();
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StartAsync(cts.Token);
        try
        {
            await bus.Publish(
                new PaymentProcessedEvent
                {
                    OrderId = pedidoId,
                    UserId = usuarioId,
                    GameId = jogoId,
                    Status = PaymentStatus.Rejected,
                    RejectionReason = "Saldo insuficiente",
                },
                ctx => ctx.MessageId = Guid.NewGuid(),
                cts.Token
            );

            (await AguardarAsync(async () => await StatusPedidoAsync(pedidoId) == 2))
                .Should()
                .BeTrue("o evento rejeitado deve marcar o pedido como Rejeitado");

            (await MotivoRecusaAsync(pedidoId)).Should().Be("Saldo insuficiente");
            (await ItensDoParAsync(usuarioId, jogoId)).Should().Be(0);
        }
        finally
        {
            await bus.StopAsync(cts.Token);
        }
    }

    private static PaymentProcessedEvent EventoAprovado(
        Guid pedidoId,
        Guid usuarioId,
        Guid jogoId
    ) =>
        new()
        {
            OrderId = pedidoId,
            UserId = usuarioId,
            GameId = jogoId,
            Status = PaymentStatus.Approved,
        };

    // Semeia direto no banco um Pedido pendente — dispensa montar jogo + POST só para ter um alvo.
    private async Task<(Guid PedidoId, Guid UsuarioId, Guid JogoId)> SemearPedidoPendenteAsync()
    {
        var usuarioId = Guid.NewGuid();
        var jogoId = Guid.NewGuid();
        var pedido = Pedido.Criar(usuarioId, jogoId, Preco.Criar(120m));

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        return (pedido.Id, usuarioId, jogoId);
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

    private async Task<string?> MotivoRecusaAsync(Guid pedidoId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db
            .Database.SqlQueryRaw<string?>(
                "SELECT motivo_recusa AS \"Value\" FROM pedidos WHERE id = {0}",
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

    // A redelivery é no-op: para provar que não duplicou, exige-se que a condição permaneça
    // verdadeira durante uma janela (não apenas num instante).
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
