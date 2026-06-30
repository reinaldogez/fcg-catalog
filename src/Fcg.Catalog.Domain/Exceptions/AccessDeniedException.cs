namespace Fcg.Catalog.Domain.Exceptions;

// Autenticado, porém sem permissão para o recurso. Mapeada para HTTP 403 — distinta do 401.
public class AccessDeniedException(string mensagem) : DomainException(mensagem) { }
