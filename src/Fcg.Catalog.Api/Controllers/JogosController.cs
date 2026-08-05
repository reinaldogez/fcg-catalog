using Fcg.Catalog.Api.Authorization;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.UseCases.Jogos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

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
    DesativarJogoUseCase desativarJogo,
    IDiagnosticContext diagnosticContext
) : ControllerBase
{
    // Propriedade estruturada da linha única que o log de requisição emite, ao lado do tempo
    // decorrido: é o que torna a diferença de latência entre acerto e falta legível lado a lado.
    private const string PropriedadeDeCache = "cacheResultado";

    private const string Acerto = "hit";

    private const string Falta = "miss";

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
        (IReadOnlyList<JogoResponse> jogos, ResultadoDeCache cache) =
            await listarJogos.ExecutarAsync(request, cancellationToken);

        RegistrarResultadoDeCache(cache);

        return Ok(jogos);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(JogoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorIdAsync(Guid id, CancellationToken cancellationToken)
    {
        (JogoResponse? jogo, ResultadoDeCache cache) = await obterJogoPorId.ExecutarAsync(
            id,
            cancellationToken
        );

        RegistrarResultadoDeCache(cache);

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

    private void RegistrarResultadoDeCache(ResultadoDeCache resultado) =>
        diagnosticContext.Set(
            PropriedadeDeCache,
            resultado == ResultadoDeCache.Hit ? Acerto : Falta
        );
}
