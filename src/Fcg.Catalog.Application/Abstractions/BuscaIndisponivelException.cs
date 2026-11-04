namespace Fcg.Catalog.Application.Abstractions;

// A busca não pode responder: cluster fora do ar, índice ainda não criado ou busca desligada no
// ambiente. Não é regra de negócio, é indisponibilidade de dependência, e por isso vive junto da
// porta que a lança, e não entre as exceções de domínio. A mensagem vai ao cliente; o motivo do
// cluster fica na exceção interna, que só vai ao log.
public sealed class BuscaIndisponivelException(string mensagem, Exception? causa = null)
    : Exception(mensagem, causa);
