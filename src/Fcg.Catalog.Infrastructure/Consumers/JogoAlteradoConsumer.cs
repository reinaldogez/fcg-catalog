using Fcg.Catalog.Application.Events;
using Fcg.Catalog.Application.UseCases.Jogos;
using MassTransit;

namespace Fcg.Catalog.Infrastructure.Consumers;

// Humble Object da fila de indexação: extrai o id do jogo e chama o use case, sem decidir nada.
// O catálogo consome o próprio evento por fila própria, como um consumidor externo faria.
public class JogoAlteradoConsumer(IndexarJogoUseCase indexar) : IConsumer<JogoAlteradoEvent>
{
    public Task Consume(ConsumeContext<JogoAlteradoEvent> context) =>
        indexar.ExecutarAsync(context.Message.JogoId, context.CancellationToken);
}
