using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class ListarJogosUseCase(IJogoRepository jogoRepository, ICacheCatalogo cacheCatalogo)
{
    private const int TamanhoMaximo = 100;

    public async Task<(IReadOnlyList<JogoResponse> Jogos, ResultadoDeCache Cache)> ExecutarAsync(
        ListarJogosRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Saneamento da paginação — evita página/tamanho inválidos chegarem ao repositório, e
        // precede o cache para que duas requisições saneadas ao mesmo par compartilhem a entrada.
        int pagina = request.Pagina < 1 ? 1 : request.Pagina;
        int tamanho = Math.Clamp(request.TamanhoPagina, 1, TamanhoMaximo);

        IReadOnlyList<JogoResponse>? doCache = await cacheCatalogo.ObterListagemAsync(
            pagina,
            tamanho,
            cancellationToken
        );

        // Nulo é o miss; lista vazia é hit legítimo de uma página sem jogos, e tratá-la como
        // ausência mandaria toda página vazia ao banco de novo.
        if (doCache is not null)
        {
            return (doCache, ResultadoDeCache.Hit);
        }

        IReadOnlyList<Jogo> jogos = await jogoRepository.ListarAsync(
            pagina,
            tamanho,
            cancellationToken
        );

        IReadOnlyList<JogoResponse> resposta = [.. jogos.Select(JogoResponse.De)];

        await cacheCatalogo.GravarListagemAsync(pagina, tamanho, resposta, cancellationToken);

        return (resposta, ResultadoDeCache.Miss);
    }
}
