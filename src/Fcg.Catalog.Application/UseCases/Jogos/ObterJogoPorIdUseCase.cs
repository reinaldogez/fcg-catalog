using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class ObterJogoPorIdUseCase(IJogoRepository jogoRepository)
{
    public async Task<JogoResponse?> ExecutarAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        Jogo? jogo = await jogoRepository.ObterPorIdAsync(id, cancellationToken);
        return jogo is null ? null : JogoResponse.De(jogo);
    }
}
