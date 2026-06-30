namespace Fcg.Catalog.Domain.Exceptions;

// Erro de regra de negócio. Mapeada para HTTP 400 no middleware da Api.
// Nunca usar exceções genéricas (ArgumentException etc.) para erro de domínio.
public class DomainException(string mensagem) : Exception(mensagem) { }
