using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class DesativarJogoUseCase(
    IJogoRepository jogoRepository,
    IUnitOfWork unitOfWork,
    ICacheCatalogo cacheCatalogo
)
{
    // Retorna se o jogo existia (para o controller distinguir 204 de 404).
    public async Task<bool> ExecutarAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Jogo? jogo = await jogoRepository.ObterPorIdAsync(id, cancellationToken);
        if (jogo is null)
            return false;

        jogo.Desativar();
        jogoRepository.Atualizar(jogo);
        await unitOfWork.SalvarAlteracoesAsync(cancellationToken);

        // Simétrico à atualização: a desativação muda o jogo nos dois lugares onde ele é servido.
        await cacheCatalogo.InvalidarListagemAsync(cancellationToken);
        await cacheCatalogo.InvalidarDetalheAsync(id, cancellationToken);

        return true;
    }
}
