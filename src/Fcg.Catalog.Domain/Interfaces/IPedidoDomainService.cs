using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Domain.Interfaces;

public interface IPedidoDomainService
{
    // Devolve o Pedido criado e o título do jogo já validado — o título alimenta o
    // fat event (GameName) sem uma segunda leitura do jogo nem título no agregado.
    Task<(Pedido Pedido, string TituloJogo)> RegistrarAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    );
}
