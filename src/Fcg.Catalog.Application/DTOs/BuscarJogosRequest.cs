namespace Fcg.Catalog.Application.DTOs;

// O termo chega como `q` na query string. Paginação no mesmo idioma da listagem.
public record BuscarJogosRequest(string? Q, int Pagina = 1, int TamanhoPagina = 20);
