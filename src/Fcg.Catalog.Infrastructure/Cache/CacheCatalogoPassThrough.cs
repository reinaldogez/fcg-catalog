using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;

namespace Fcg.Catalog.Infrastructure.Cache;

// Forma desligada do config-gate: sem host de Redis a aplicação sobe e os endpoints respondem
// direto do banco. Toda leitura é miss e toda escrita é descartada.
//
// Não é cache em memória de propósito: um cache local por réplica divergiria entre instâncias e
// esconderia a ausência da dependência atrás de acertos que só valem numa delas.
public sealed class CacheCatalogoPassThrough : ICacheCatalogo
{
    public Task<IReadOnlyList<JogoResponse>?> ObterListagemAsync(
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<JogoResponse>?>(null);

    public Task GravarListagemAsync(
        int pagina,
        int tamanhoPagina,
        IReadOnlyList<JogoResponse> jogos,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task<JogoResponse?> ObterDetalheAsync(
        Guid jogoId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<JogoResponse?>(null);

    public Task GravarDetalheAsync(
        JogoResponse jogo,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task InvalidarListagemAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task InvalidarDetalheAsync(Guid jogoId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
