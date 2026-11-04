namespace Fcg.Catalog.Application.Events;

// Evento interno do catálogo: um jogo foi criado, editado ou desativado. Magro de propósito — só
// diz qual jogo mudou, e o consumidor relê o estado atual na fonte da verdade. Um evento com o
// estado embutido obrigaria o consumidor a decidir qual de dois estados entregues fora de ordem
// é o mais novo.
public record JogoAlteradoEvent
{
    public Guid JogoId { get; init; }
}
