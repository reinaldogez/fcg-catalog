namespace Fcg.Catalog.Domain.Entities;

// Biblioteca como item (uma linha por par usuário×jogo), não como raiz-coleção.
// Criado ao consumir um pagamento aprovado.
public class ItemBiblioteca
{
    // EF materializa por aqui.
    private ItemBiblioteca() { }

    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid JogoId { get; private set; }
    public DateTime AdicionadoEm { get; private set; }

    public static ItemBiblioteca Criar(Guid usuarioId, Guid jogoId)
    {
        return new ItemBiblioteca
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            JogoId = jogoId,
            AdicionadoEm = DateTime.UtcNow,
        };
    }
}
