using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class AtualizarJogoUseCase(IJogoRepository jogoRepository, IUnitOfWork unitOfWork)
{
    public async Task<JogoResponse?> ExecutarAsync(
        Guid id,
        AtualizarJogoRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Jogo? jogo = await jogoRepository.ObterPorIdAsync(id, cancellationToken);
        if (jogo is null)
            return null;

        jogo.Atualizar(
            Titulo.Criar(request.Titulo),
            Preco.Criar(request.Preco),
            request.Descricao,
            request.Desenvolvedora,
            request.DataLancamento
        );

        jogoRepository.Atualizar(jogo);
        await unitOfWork.SalvarAlteracoesAsync(cancellationToken);

        return JogoResponse.De(jogo);
    }
}
