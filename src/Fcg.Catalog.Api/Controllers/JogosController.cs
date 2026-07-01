using Fcg.Catalog.Api.Authorization;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Jogos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fcg.Catalog.Api.Controllers;

[ApiController]
[Route("api/jogos")]
[Authorize]
[EnableRateLimiting("fixed")]
public class JogosController(
    CriarJogoUseCase criarJogo,
    ListarJogosUseCase listarJogos,
    ObterJogoPorIdUseCase obterJogoPorId,
    AtualizarJogoUseCase atualizarJogo,
    DesativarJogoUseCase desativarJogo
) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(JogoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CriarAsync(
        CriarJogoRequest request,
        CancellationToken cancellationToken
    )
    {
        JogoResponse jogo = await criarJogo.ExecutarAsync(request, cancellationToken);
        return CreatedAtAction(nameof(ObterPorIdAsync), new { id = jogo.Id }, jogo);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<JogoResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarAsync(
        [FromQuery] ListarJogosRequest request,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<JogoResponse> jogos = await listarJogos.ExecutarAsync(
            request,
            cancellationToken
        );
        return Ok(jogos);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(JogoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorIdAsync(Guid id, CancellationToken cancellationToken)
    {
        JogoResponse? jogo = await obterJogoPorId.ExecutarAsync(id, cancellationToken);
        return jogo is null ? NotFound() : Ok(jogo);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(JogoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AtualizarAsync(
        Guid id,
        AtualizarJogoRequest request,
        CancellationToken cancellationToken
    )
    {
        JogoResponse? jogo = await atualizarJogo.ExecutarAsync(id, request, cancellationToken);
        return jogo is null ? NotFound() : Ok(jogo);
    }

    [HttpPatch("{id:guid}/desativar")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DesativarAsync(Guid id, CancellationToken cancellationToken)
    {
        bool existia = await desativarJogo.ExecutarAsync(id, cancellationToken);
        return existia ? NoContent() : NotFound();
    }
}
