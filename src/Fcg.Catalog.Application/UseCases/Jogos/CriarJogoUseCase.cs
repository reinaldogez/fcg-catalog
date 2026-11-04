using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.Events;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.ValueObjects;
using MassTransit;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class CriarJogoUseCase(
    IJogoRepository jogoRepository,
    IUnitOfWork unitOfWork,
    ICacheCatalogo cacheCatalogo,
    IPublishEndpoint publishEndpoint
)
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

        // Publish antes do commit: a linha do outbox cai na mesma transação que o jogo, e a
        // indexação nunca fica sem o evento de um jogo gravado. A entrega ao broker é pós-commit.
        await publishEndpoint.Publish(
            new JogoAlteradoEvent { JogoId = jogo.Id },
            cancellationToken
        );

        await unitOfWork.SalvarAlteracoesAsync(cancellationToken);

        // Depois do commit: invalidar antes deixaria uma janela em que a leitura recacheia a
        // listagem sem o jogo novo. Só a listagem, porque o detalhe de um id recém-criado ainda
        // não tem chave a remover.
        await cacheCatalogo.InvalidarListagemAsync(cancellationToken);

        return JogoResponse.De(jogo);
    }
}
