using Fcg.Catalog.Tests.Integration.Fixtures;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Reqnroll.BoDi;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Fcg.Catalog.Tests.Bdd.Support;

[Binding]
public class Hooks(IObjectContainer objectContainer)
{
    private static CatalogApiFactory s_factory = null!;
    private static IBusControl s_bus = null!;

    // A fixture de integração sobe Postgres + RabbitMQ e remove os hosted services do MassTransit
    // (o sweeper do Outbox dá deadlock com o reset de banco). O bus é iniciado uma vez por run
    // para o consumer de pagamento ficar ativo durante os cenários; o reset entre cenários ocorre
    // com o bus ocioso (cada cenário só termina após observar o estado final do pedido).
    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        s_factory = new CatalogApiFactory();
        await ((IAsyncLifetime)s_factory).InitializeAsync();

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));
        s_bus = s_factory.Services.GetRequiredService<IBusControl>();
        await s_bus.StartAsync(cts.Token);
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));
        await s_bus.StopAsync(cts.Token);
        await ((IAsyncLifetime)s_factory).DisposeAsync();
    }

    [BeforeScenario]
    public async Task BeforeScenario()
    {
        await s_factory.ResetAsync();

        HttpClient client = s_factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        objectContainer.RegisterInstanceAs(client);
        objectContainer.RegisterInstanceAs(new CenarioEstado());
        objectContainer.RegisterInstanceAs(s_factory.Services.GetRequiredService<IBus>());
    }
}
