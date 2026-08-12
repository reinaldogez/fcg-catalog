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

        // email/nome viajam no fat event até a notificação de compra e o evento é imutável depois
        // de publicado: ausentes, nada corrige o e-mail sem destinatário depois. Falha alto aqui.
        if (User.FindFirst(JwtRegisteredClaimNames.Email)?.Value is not { Length: > 0 } email)
            throw new DomainAuthException("Token sem claim 'email' válida.");

        if (User.FindFirst(JwtRegisteredClaimNames.Name)?.Value is not { Length: > 0 } nome)
            throw new DomainAuthException("Token sem claim 'name' válida.");

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
