namespace Fcg.Catalog.Application.DTOs;

// Só o jogo: UsuarioId/email/nome vêm das claims, nunca do body.
public record CriarPedidoRequest(Guid JogoId);
