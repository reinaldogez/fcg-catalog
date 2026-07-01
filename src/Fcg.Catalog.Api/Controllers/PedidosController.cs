using System.Security.Claims;
using Fcg.Catalog.Api.Authorization;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Fcg.Catalog.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
[Authorize]
[EnableRateLimiting("fixed")]
public class PedidosController(
    CriarPedidoUseCase criarPedido,
    ObterPedidoPorIdUseCase obterPedidoPorId,
    IAuthorizationService authorizationService
) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(PedidoResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CriarAsync(
        CriarPedidoRequest request,
        CancellationToken cancellationToken
    )
    {
        // sub é obrigatório: sem ele não há em nome de quem criar o pedido (nunca vem do body).
        if (
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is not string sub
            || !Guid.TryParse(sub, out Guid usuarioId)
        )
            throw new DomainAuthException("Token sem claim 'sub' válida.");

        string email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        string nome = User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // Carrega (404 antes de autorizar), autoriza pelo dono do agregado, então responde.
        Pedido? pedido = await obterPedidoPorId.ObterEntidadeAsync(id, cancellationToken);
        if (pedido is null)
            return NotFound();

        AuthorizationResult resultado = await authorizationService.AuthorizeAsync(
            User,
            pedido,
            new PedidoOwnershipRequirement()
        );
        if (!resultado.Succeeded)
            return Forbid();

        return Ok(PedidoResponse.De(pedido));
    }
}
