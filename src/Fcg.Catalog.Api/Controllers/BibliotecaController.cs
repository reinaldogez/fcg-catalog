using Fcg.Catalog.Api.Authorization;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Biblioteca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fcg.Catalog.Api.Controllers;

[ApiController]
[Route("api/biblioteca")]
[EnableRateLimiting("fixed")]
public class BibliotecaController(ObterBibliotecaDoUsuarioUseCase obterBiblioteca) : ControllerBase
{
    [HttpGet("{usuarioId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SelfOrAdmin)]
    [ProducesResponseType(typeof(IReadOnlyList<ItemBibliotecaResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ObterAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ItemBibliotecaResponse> itens = await obterBiblioteca.ExecutarAsync(
            usuarioId,
            cancellationToken
        );
        return Ok(itens);
    }
}
