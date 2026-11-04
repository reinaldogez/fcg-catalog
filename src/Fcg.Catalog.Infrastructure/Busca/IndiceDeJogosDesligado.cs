using Fcg.Catalog.Application.Abstractions;

namespace Fcg.Catalog.Infrastructure.Busca;

// Forma desligada do config-gate: sem endpoint de busca, a escrita é descartada e o cluster nunca
// está pronto. Com isso o consumer de indexação confirma a mensagem sem efeito, e a topologia do
// broker é a mesma em todo ambiente — registrar o consumer só com a busca ligada deixaria uma
// fila órfã num broker persistente.
public sealed class IndiceDeJogosDesligado : IIndiceDeJogos, IPreparacaoDoIndice
{
    public Task<ResultadoEscritaIndice> GravarAsync(
        DocumentoJogo documento,
        long versao,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(ResultadoEscritaIndice.Aplicada);

    public Task<ResultadoEscritaIndice> RemoverAsync(
        Guid id,
        long? versao,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(ResultadoEscritaIndice.Aplicada);

    public Task<bool> EstaSaudavelAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task GarantirAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<long> ContarAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0L);
}
