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
            x.AddConsumer<PaymentProcessedProjectionConsumer>();

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
                    bool useSsl = RabbitMqSsl.Habilitado(configuration);

                    cfg.Host(
                        host,
                        port,
                        "/",
                        h =>
                        {
                            h.Username(username);
                            h.Password(password);

                            // O broker gerenciado só aceita amqps; o local segue em texto claro
                            // com a chave ausente. Sem fixar versão de protocolo nem afrouxar a
                            // validação de certificado: a negociação e a cadeia ficam no default.
                            if (useSsl)
                            {
                                h.UseSsl(_ => { });
                            }
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
                        e =>
                        {
                            // Inbox no endpoint: deduplica a mesma mensagem (mesmo MessageId) sob
                            // redelivery e envolve as escritas do consumer numa transação única do
                            // mesmo CatalogDbContext scoped — o commit é do harness, não do use case.
                            e.UseEntityFrameworkOutbox<CatalogDbContext>(context);
                            e.ConfigureConsumer<PaymentProcessedConsumer>(context);
                        }
                    );

                    // Fila própria para a projeção, com bind na mesma exchange: o caminho de falha
                    // fica independente do crédito na fonte da verdade.
                    cfg.ReceiveEndpoint(
                        "payment-processed.fcg-catalog-projections",
                        e =>
                        {
                            // Sem Inbox, de propósito. O Inbox protege efeito não-idempotente, e o
                            // desta fila é idempotente por construção: todo atributo do item é
                            // função pura do evento, então reentrega reescreve o mesmo item.
                            // Declará-lo abriria transação no PostgreSQL num consumer que não
                            // escreve uma linha nele — e sem atomicidade real, já que a escrita no
                            // read model fica fora dessa transação de qualquer forma.
                            //
                            // Retry curto porque a dependência é remota: throttling, renovação de
                            // credencial e reset de TLS se resolvem sozinhos em segundos. O
                            // endpoint de crédito acima não repete porque fala com o banco do
                            // próprio cluster, onde a falha é sistêmica ou determinística e
                            // repetir em seis segundos não muda o desfecho. Esgotadas as
                            // tentativas, a mensagem vai para a fila de erro do broker.
                            //
                            // Sem limite de concorrência: a projeção é ordem-independente — cada
                            // item tem chave própria, sem agregação e sem contador.
                            e.UseMessageRetry(r =>
                            {
                                r.Interval(3, TimeSpan.FromSeconds(2));

                                // Evento malformado é falha determinística: repetir não muda o
                                // desfecho e só atrasa a ida para a fila de erro.
                                r.Ignore<ArgumentException>();
                                r.Ignore<FormatException>();
                            });

                            e.ConfigureConsumer<PaymentProcessedProjectionConsumer>(context);
                        }
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
