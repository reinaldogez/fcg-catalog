using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class ObterJogoPorIdUseCase(IJogoRepository jogoRepository, ICacheCatalogo cacheCatalogo)
{
    public async Task<(JogoResponse? Jogo, ResultadoDeCache Cache)> ExecutarAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        JogoResponse? doCache = await cacheCatalogo.ObterDetalheAsync(id, cancellationToken);

        if (doCache is not null)
        {
            return (doCache, ResultadoDeCache.Hit);
        }

        Jogo? jogo = await jogoRepository.ObterPorIdAsync(id, cancellationToken);

        // Ausência não se cacheia: bastaria pedir identificadores aleatórios para poluir o cache, e
        // a criação de um jogo não teria como remover a chave de um id que ainda não existia.
        if (jogo is null)
        {
            return (null, ResultadoDeCache.Miss);
        }

        var resposta = JogoResponse.De(jogo);

        await cacheCatalogo.GravarDetalheAsync(resposta, cancellationToken);

        return (resposta, ResultadoDeCache.Miss);
    }
}
