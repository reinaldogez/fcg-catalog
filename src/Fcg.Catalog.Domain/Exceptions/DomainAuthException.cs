namespace Fcg.Catalog.Domain.Exceptions;

// Falha de autenticação (credencial ausente/ inválida). Mapeada para HTTP 401.
public class DomainAuthException(string mensagem) : DomainException(mensagem) { }
