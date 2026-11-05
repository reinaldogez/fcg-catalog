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
    BuscarJogosUseCase buscarJogos,
    IDiagnosticContext diagnosticContext
) : ControllerBase
{
    private const string DescricaoDaBusca =
        "Busca jogos do catálogo no índice de busca — uma projeção em OpenSearch alimentada pelos "
        + "eventos de criação, edição e desativação de jogo. A busca tolera erros de digitação e "
        + "ordena os resultados por relevância; o campo `relevancia` só é comparável entre itens "
        + "da mesma resposta. O índice reflete o catálogo com um pequeno atraso: um jogo "
        + "recém-criado ou editado pode ainda não constar, ou constar com o estado anterior. "
        + "Jogos desativados não aparecem. Para o estado atual de um jogo específico, consulte "
        + "`GET /api/jogos/{id}`, que lê a fonte da verdade.";

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

    // Segmento literal: não é guid, então não cai no detalhe. Sem registro de cache, porque a
    // busca não passa pelo cache.
    [HttpGet("search")]
    [EndpointDescription(DescricaoDaBusca)]
    [ProducesResponseType(
        typeof(IReadOnlyList<ResultadoBuscaJogoResponse>),
        StatusCodes.Status200OK
    )]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BuscarAsync(
        [FromQuery] BuscarJogosRequest request,
        CancellationToken cancellationToken
    ) => Ok(await buscarJogos.ExecutarAsync(request, cancellationToken));

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
