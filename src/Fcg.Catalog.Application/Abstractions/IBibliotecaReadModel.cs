namespace Fcg.Catalog.Application.Abstractions;

// Lado de consulta da biblioteca. Existe separada do repositório do write model porque a
// apresentação e a invariante de negócio deixaram de caber na mesma abstração: a invariante
// pergunta se o par usuário-jogo existe na fonte da verdade, e esta porta serve o item já
// desnormalizado.
public interface IBibliotecaReadModel
{
    Task<IReadOnlyList<ItemBibliotecaProjetado>> ListarPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default
    );
}
