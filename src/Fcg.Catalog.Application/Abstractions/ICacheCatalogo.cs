using Fcg.Catalog.Application.DTOs;

namespace Fcg.Catalog.Application.Abstractions;

// Porta do cache do catálogo, em termos de domínio: quem consome pede "a lista da página 1 com 20"
// ou manda invalidar a listagem, e nunca vê chave, versão nem prefixo — composição de chave e
// versionamento são responsabilidade do adaptador.
//
// Ausência devolvida como nulo é o miss; lista vazia é hit legítimo de uma página sem jogos.
public interface ICacheCatalogo
{
    Task<IReadOnlyList<JogoResponse>?> ObterListagemAsync(
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default
    );

    Task GravarListagemAsync(
        int pagina,
        int tamanhoPagina,
        IReadOnlyList<JogoResponse> jogos,
        CancellationToken cancellationToken = default
    );

    Task<JogoResponse?> ObterDetalheAsync(
        Guid jogoId,
        CancellationToken cancellationToken = default
    );

    Task GravarDetalheAsync(JogoResponse jogo, CancellationToken cancellationToken = default);

    Task InvalidarListagemAsync(CancellationToken cancellationToken = default);

    Task InvalidarDetalheAsync(Guid jogoId, CancellationToken cancellationToken = default);
}
