using Fcg.Catalog.Domain.Enums;
using Fcg.Catalog.Domain.Exceptions;
using Fcg.Catalog.Domain.ValueObjects;

namespace Fcg.Catalog.Domain.Entities;

public class Pedido
{
    // EF materializa por aqui.
    private Pedido() { }

    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid JogoId { get; private set; }

    // Snapshot imutável de Jogo.Preco no instante da criação.
    public Preco Valor { get; private set; } = null!;
    public StatusPedido Status { get; private set; }
    public string? MotivoRecusa { get; private set; }
    public DateTime CriadoEm { get; private set; }

    public static Pedido Criar(Guid usuarioId, Guid jogoId, Preco valor)
    {
        return new Pedido
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            JogoId = jogoId,
            Valor = valor,
            Status = StatusPedido.Pendente,
            CriadoEm = DateTime.UtcNow,
        };
    }

    public void Aprovar()
    {
        if (Status != StatusPedido.Pendente)
            throw new DomainException("Só é possível aprovar um pedido pendente.");

        Status = StatusPedido.Aprovado;
    }

    public void Rejeitar(string motivo)
    {
        if (Status != StatusPedido.Pendente)
            throw new DomainException("Só é possível rejeitar um pedido pendente.");

        Status = StatusPedido.Rejeitado;
        MotivoRecusa = motivo;
    }

    // Tell-Don't-Ask: o agregado responde sobre a própria propriedade.
    public bool PertenceAo(Guid usuarioId) => UsuarioId == usuarioId;
}
