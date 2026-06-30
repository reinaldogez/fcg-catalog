using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Application.DTOs;

public record ItemBibliotecaResponse(Guid Id, Guid UsuarioId, Guid JogoId, DateTime AdicionadoEm)
{
    public static ItemBibliotecaResponse De(ItemBiblioteca item) =>
        new(item.Id, item.UsuarioId, item.JogoId, item.AdicionadoEm);
}
