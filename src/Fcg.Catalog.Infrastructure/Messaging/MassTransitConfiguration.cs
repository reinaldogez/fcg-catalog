using Fcg.Catalog.Infrastructure.Consumers;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fcg.Catalog.Infrastructure.Messaging;

public static class MassTransitConfiguration
{
    public static IServiceCollection AddCatalogMessaging(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddMassTransit(x =>
        {
            // Outbox transacional sobre o mesmo CatalogDbContext: a linha do evento cai na
            // transação do agregado (publish) e o Inbox deduplica entregas repetidas (consume).
            x.AddEntityFrameworkOutbox<CatalogDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.AddConsumer<PaymentProcessedConsumer>();

            x.UsingRabbitMq(
                (context, cfg) =>
                {
                    // Host por campos separados (Host/Port não-sensível via ConfigMap;
                    // Username/Password via Secret) — fail-fast se faltar o essencial.
                    string host =
                        configuration["RabbitMq:Host"]
                        ?? throw new InvalidOperationException("RabbitMq:Host não configurado.");
                    string username =
                        configuration["RabbitMq:Username"]
                        ?? throw new InvalidOperationException(
                            "RabbitMq:Username não configurado."
                        );
                    string password =
                        configuration["RabbitMq:Password"]
                        ?? throw new InvalidOperationException(
                            "RabbitMq:Password não configurado."
                        );
                    ushort port = ushort.TryParse(configuration["RabbitMq:Port"], out ushort p)
                        ? p
                        : (ushort)5672;

                    cfg.Host(
                        host,
                        port,
                        "/",
                        h =>
                        {
                            h.Username(username);
                            h.Password(password);
                        }
                    );

                    // Nome de exchange/fila vive no bus, não no contrato (Fcg.Contracts são
                    // records puros, sem [EntityName]).
                    // Publish: order-placed (fanout).
                    cfg.Message<OrderPlacedEvent>(m => m.SetEntityName("order-placed"));
                    cfg.Publish<OrderPlacedEvent>(p => p.ExchangeType = "fanout");

                    // Consume: bind da fila na exchange payment-processed (publicada pelo payments).
                    cfg.Message<PaymentProcessedEvent>(m => m.SetEntityName("payment-processed"));

                    // ReceiveEndpoint explícito (não kebab formatter): entrega o sufixo .fcg-catalog
                    // da fila consumidora — inequívoco na Management UI.
                    cfg.ReceiveEndpoint(
                        "payment-processed.fcg-catalog",
                        e => e.ConfigureConsumer<PaymentProcessedConsumer>(context)
                    );
                }
            );
        });

        // O check do bus do MassTransit nasce com a tag "ready". Removê-la: o readiness fica
        // só-Postgres — o Outbox desacopla a entrega do broker, então broker fora não deve
        // derrubar a prontidão (o pedido ainda é criado e o evento fica seguro na Outbox).
        services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            foreach (
                HealthCheckRegistration registro in options.Registrations.Where(r =>
                    r.Name.StartsWith("masstransit", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                registro.Tags.Remove("ready");
            }
        });

        return services;
    }
}
