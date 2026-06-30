using System.Security.Claims;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Pedidos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fcg.Catalog.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
[EnableRateLimiting("fixed")]
public class PedidosController(
    CriarPedidoUseCase criarPedido,
    ObterPedidoPorIdUseCase obterPedidoPorId
) : ControllerBase
{
    // O identitário virá das claims do JWT quando a autenticação for cabeada. A leitura de
    // claims abaixo já é a forma final; só o fallback some quando o pipeline popular User.
    private static readonly Guid s_usuarioPlaceholder = Guid.Parse(
        "00000000-0000-0000-0000-000000000001"
    );
    private const string EmailPlaceholder = "placeholder@fcg.local";
    private const string NomePlaceholder = "Usuário Placeholder";

    [HttpPost]
    [ProducesResponseType(typeof(PedidoResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CriarAsync(
        CriarPedidoRequest request,
        CancellationToken cancellationToken
    )
    {
        Guid usuarioId =
            User.FindFirst("sub")?.Value is string sub && Guid.TryParse(sub, out Guid id)
                ? id
                : s_usuarioPlaceholder;
        string email = User.FindFirst(ClaimTypes.Email)?.Value ?? EmailPlaceholder;
        string nome = User.FindFirst(ClaimTypes.Name)?.Value ?? NomePlaceholder;

        PedidoResponse pedido = await criarPedido.ExecutarAsync(
            request,
            usuarioId,
            email,
            nome,
            cancellationToken
        );
        return AcceptedAtAction(nameof(ObterPorIdAsync), new { id = pedido.Id }, pedido);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PedidoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorIdAsync(Guid id, CancellationToken cancellationToken)
    {
        PedidoResponse? pedido = await obterPedidoPorId.ExecutarAsync(id, cancellationToken);
        return pedido is null ? NotFound() : Ok(pedido);
    }
}
