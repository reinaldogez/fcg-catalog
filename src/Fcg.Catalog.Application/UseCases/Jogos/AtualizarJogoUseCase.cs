using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class AtualizarJogoUseCase(
    IJogoRepository jogoRepository,
    IUnitOfWork unitOfWork,
    ICacheCatalogo cacheCatalogo
)
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

        // As duas invalidações: o jogo alterado aparece no detalhe e dentro das páginas da
        // listagem, e deixar uma delas de fora serviria o valor antigo por um dos dois caminhos.
        await cacheCatalogo.InvalidarListagemAsync(cancellationToken);
        await cacheCatalogo.InvalidarDetalheAsync(id, cancellationToken);

        return JogoResponse.De(jogo);
    }
}
