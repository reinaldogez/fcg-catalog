using Fcg.Contracts.Events;
using MassTransit;

namespace Fcg.Catalog.Infrastructure.Consumers;

// Humble Object da borda de mensageria: materializa a fila payment-processed.fcg-catalog e
// será o ponto de despacho do fechamento da saga. O corpo é preenchido quando a lógica de
// fechamento (despacho por Status para os use cases de aprovar/rejeitar, sob a transação única
// do Inbox) for implementada — aqui o consumer só existe para o bus declarar a topologia.
public class PaymentProcessedConsumer : IConsumer<PaymentProcessedEvent>
{
    public Task Consume(ConsumeContext<PaymentProcessedEvent> context) => Task.CompletedTask;
}
