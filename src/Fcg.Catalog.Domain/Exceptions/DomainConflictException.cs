namespace Fcg.Catalog.Domain.Exceptions;

// Conflito com o estado atual do recurso (ex.: duplicidade). Mapeada para HTTP 409.
public class DomainConflictException(string mensagem) : DomainException(mensagem) { }
