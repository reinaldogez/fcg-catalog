using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class CriarJogoUseCase(IJogoRepository jogoRepository, IUnitOfWork unitOfWork)
{
    public async Task<JogoResponse> ExecutarAsync(
        CriarJogoRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var jogo = Jogo.Criar(
            Titulo.Criar(request.Titulo),
            Preco.Criar(request.Preco),
            request.Descricao,
            request.Desenvolvedora,
            request.DataLancamento
        );

        await jogoRepository.AdicionarAsync(jogo, cancellationToken);
        await unitOfWork.SalvarAlteracoesAsync(cancellationToken);

        return JogoResponse.De(jogo);
    }
}
