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
    // A janela de consistência eventual é contrato, não defeito, e por isso é declarada ao cliente
    // no próprio documento da API. A última frase é o que faz a descrição valer: dá o caminho para
    // acompanhar a própria escrita, em vez de só avisar que ele não existe aqui.
    [EndpointDescription(
        "Retorna a biblioteca do usuário a partir do modelo de leitura — uma projeção em DynamoDB "
            + "alimentada pelo evento de pagamento aprovado. A resposta reflete o estado da projeção "
            + "no instante da consulta: uma compra recém-aprovada pode ainda não constar na lista. "
            + "Isso é consistência eventual esperada, não erro — a lista converge assim que a "
            + "projeção processa o evento correspondente. Para acompanhar o desfecho de uma compra "
            + "específica, consulte `GET /api/pedidos/{id}`, que lê a fonte da verdade."
    )]
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
