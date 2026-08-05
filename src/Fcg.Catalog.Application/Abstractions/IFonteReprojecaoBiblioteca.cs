namespace Fcg.Catalog.Application.Abstractions;

// Fonte da reconstrução do modelo de leitura: a base relacional, já reunida na forma que a porta de
// escrita consome. A leitura é paginada porque a reconstrução é total e materializar a biblioteca
// inteira de uma vez não tem teto.
public interface IFonteReprojecaoBiblioteca
{
    Task<IReadOnlyList<LinhaReprojecaoBiblioteca>> LerPaginaAsync(
        int deslocamento,
        int tamanho,
        CancellationToken cancellationToken = default
    );
}
