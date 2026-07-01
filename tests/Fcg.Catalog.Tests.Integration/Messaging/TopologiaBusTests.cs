using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Messaging;

// Os hosted services do MassTransit são removidos na fixture (o sweeper do Outbox dá deadlock
// com o reset de banco), então o bus não sobe sozinho. Aqui ele é iniciado sob demanda contra o
// RabbitMQ real: iniciar sem erro de topologia prova que o ReceiveEndpoint declara a fila
// payment-processed.fcg-catalog e o bind na exchange payment-processed.
[Collection(IntegrationCollection.Name)]
public class TopologiaBusTests(CatalogApiFactory factory)
{
    [Fact]
    public async Task DeveDeclararAFilaConsumidoraSemErroDeTopologia()
    {
        IBusControl bus = factory.Services.GetRequiredService<IBusControl>();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        Func<Task> iniciarEParar = async () =>
        {
            await bus.StartAsync(cts.Token);
            await bus.StopAsync(cts.Token);
        };

        await iniciarEParar.Should().NotThrowAsync();
    }
}
