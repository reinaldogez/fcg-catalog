namespace Fcg.Catalog.Application.Abstractions;

// Ciclo de vida do índice de busca, usado pela reconstrução antes de ler a fonte da verdade.
public interface IPreparacaoDoIndice
{
    // Nunca lança: qualquer falha conta como não saudável, para que quem espera o cluster possa
    // sondar em laço sem tratar exceção como fluxo normal.
    Task<bool> EstaSaudavelAsync(CancellationToken cancellationToken = default);

    // Impede que uma escrita crie o índice por conta própria e cria o índice com o mapeamento do
    // serviço se ele ainda não existe. Idempotente, inclusive com duas chamadas simultâneas.
    Task GarantirAsync(CancellationToken cancellationToken = default);

    Task<long> ContarAsync(CancellationToken cancellationToken = default);
}
