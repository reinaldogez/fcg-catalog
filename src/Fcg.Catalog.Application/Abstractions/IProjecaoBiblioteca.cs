namespace Fcg.Catalog.Application.Abstractions;

// Lado de escrita do modelo de leitura. A operação é uma só, e é a mesma para quem projeta a
// partir do evento e para quem reconstrói a partir da fonte da verdade: dar duas operações ao
// mesmo item significaria duas mecânicas de escrita a manter em sincronia.
public interface IProjecaoBiblioteca
{
    Task ProjetarAsync(ItemBibliotecaProjetado item, CancellationToken cancellationToken = default);
}
