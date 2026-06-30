using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Contracts.Events;
using MassTransit;

namespace Fcg.Catalog.Application.UseCases.Pedidos;

// Inicia a saga de compra: cria o Pedido e publica o evento via Outbox transacional.
public class CriarPedidoUseCase(
    IPedidoDomainService pedidoDomainService,
    IPedidoRepository pedidoRepository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint
)
{
    public async Task<PedidoResponse> ExecutarAsync(
        CriarPedidoRequest request,
        Guid usuarioId,
        string usuarioEmail,
        string usuarioNome,
        CancellationToken cancellationToken = default
    )
    {
        Pedido pedido = await pedidoDomainService.RegistrarAsync(
            usuarioId,
            request.JogoId,
            cancellationToken
        );

        await pedidoRepository.AdicionarAsync(pedido, cancellationToken);

        // Publish ANTES do commit: a linha do Outbox cai na MESMA transação que o Pedido
        // (atomicidade; evita dual-write). A entrega ao broker é background pós-commit.
        await publishEndpoint.Publish(
            new OrderPlacedEvent
            {
                OrderId = pedido.Id,
                UserId = usuarioId,
                UserEmail = usuarioEmail,
                UserName = usuarioNome,
                GameId = pedido.JogoId,
                GameName = string.Empty,
                Price = pedido.Valor.Valor,
            },
            cancellationToken
        );

        await unitOfWork.SalvarAlteracoesAsync(cancellationToken);

        return PedidoResponse.De(pedido);
    }
}
