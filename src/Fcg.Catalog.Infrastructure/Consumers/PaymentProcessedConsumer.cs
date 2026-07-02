using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Contracts.Enums;
using Fcg.Contracts.Events;
using MassTransit;

namespace Fcg.Catalog.Infrastructure.Consumers;

// Humble Object da borda de mensageria: materializa a fila payment-processed.fcg-catalog e
// despacha o fechamento da saga por Status. Não decide nada — extrai os campos e chama o use
// case; a inteligência (transições, invariantes) vive no domínio. Não comita nem trata
// idempotência à mão: o Inbox (UseEntityFrameworkOutbox no endpoint) deduplica a redelivery e
// o harness comita as escritas numa transação única.
public class PaymentProcessedConsumer(AprovarPedidoUseCase aprovar, RejeitarPedidoUseCase rejeitar)
    : IConsumer<PaymentProcessedEvent>
{
    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        PaymentProcessedEvent evento = context.Message;
        CancellationToken ct = context.CancellationToken;

        switch (evento.Status)
        {
            case PaymentStatus.Approved:
                await aprovar.ExecutarAsync(evento.OrderId, ct);
                break;
            case PaymentStatus.Rejected:
                await rejeitar.ExecutarAsync(evento.OrderId, evento.RejectionReason ?? "", ct);
                break;
            default:
                throw new InvalidOperationException(
                    $"Status de pagamento não suportado: {evento.Status}."
                );
        }
    }
}
