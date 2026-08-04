using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Domain.Interfaces;

// Escrita pura mais a pergunta da invariante. A listagem de apresentação saiu daqui quando o
// caminho de leitura passou a ser servido pelo modelo de leitura: o que sobra é o que a regra de
// negócio precisa, e ela continua perguntando à fonte da verdade.
public interface IItemBibliotecaRepository
{
    Task<bool> ExisteAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    );
    Task AdicionarAsync(ItemBiblioteca item, CancellationToken cancellationToken = default);
}
