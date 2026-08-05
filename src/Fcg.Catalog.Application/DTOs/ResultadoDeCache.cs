namespace Fcg.Catalog.Application.DTOs;

// Origem da resposta de uma leitura com cache-aside. O use case devolve isto junto do conteúdo
// porque quem enriquece o log da requisição é a borda HTTP, e ela não tem como saber sozinha se a
// resposta veio do cache ou do banco.
public enum ResultadoDeCache
{
    Hit,
    Miss,
}
