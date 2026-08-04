using Fcg.Catalog.Application.UseCases.Biblioteca;
using Fcg.Contracts.Enums;
using Fcg.Contracts.Events;
using MassTransit;

namespace Fcg.Catalog.Infrastructure.Consumers;

// Humble Object da fila de projeção: extrai os campos do evento e chama o use case, sem decidir
// nada. Não resolve o contexto do banco relacional — o efeito desta fila é só a escrita no modelo
// de leitura, e o crédito na fonte da verdade tem fila e consumer próprios.
public class PaymentProcessedProjectionConsumer(ProjetarItemBibliotecaUseCase projetar)
    : IConsumer<PaymentProcessedEvent>
{
    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        PaymentProcessedEvent evento = context.Message;

        // Só a compra concluída entra na biblioteca. Pagamento recusado não tem o que projetar, e
        // lançar encheria a fila de erro com não-falha: retorno normal, que o bus confirma.
        if (evento.Status != PaymentStatus.Approved)
        {
            return;
        }

        await projetar.ExecutarAsync(
            evento.UserId,
            evento.GameId,
            evento.OrderId,
            evento.GameName,
            evento.Price,
            evento.OccurredAt.UtcDateTime,
            context.CancellationToken
        );
    }
}
