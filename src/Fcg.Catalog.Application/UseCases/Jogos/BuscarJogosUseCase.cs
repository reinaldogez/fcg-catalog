using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Exceptions;

namespace Fcg.Catalog.Application.UseCases.Jogos;

// Busca no índice, sem repositório e sem cache: o índice já é o modelo de leitura otimizado, e a
// resposta não passa pela fonte da verdade.
public class BuscarJogosUseCase(IBuscaDeJogos buscaDeJogos)
{
    // O mesmo teto do título: um termo maior que qualquer título não tem o que encontrar.
    private const int TamanhoMaximoDoTermo = 200;

    private const int TamanhoMaximo = 100;

    // O teto padrão do motor para deslocamento mais tamanho: além dele a consulta é recusada pelo
    // cluster, e a recusa viraria indisponibilidade em vez de requisição inválida.
    private const int JanelaMaxima = 10_000;

    public async Task<IReadOnlyList<ResultadoBuscaJogoResponse>> ExecutarAsync(
        BuscarJogosRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Sem termo não há busca: a listagem já cobre esse caso, e um "casa tudo" daria uma
        // segunda listagem com outra ordem.
        string termo = request.Q?.Trim() ?? string.Empty;

        if (termo.Length == 0)
            throw new DomainException("O termo de busca é obrigatório.");

        if (termo.Length > TamanhoMaximoDoTermo)
            throw new DomainException(
                $"O termo de busca deve ter no máximo {TamanhoMaximoDoTermo} caracteres."
            );

        // O mesmo saneamento da listagem: tamanho fora da faixa é ajustado, não recusado.
        int pagina = request.Pagina < 1 ? 1 : request.Pagina;
        int tamanho = Math.Clamp(request.TamanhoPagina, 1, TamanhoMaximo);

        // A janela é a dos valores saneados, que são os que chegam ao motor. Em long: página
        // perto do máximo de int estouraria a multiplicação.
        long janela = ((long)pagina - 1) * tamanho + tamanho;

        if (janela > JanelaMaxima)
            throw new DomainException(
                $"A busca alcança no máximo os {JanelaMaxima} primeiros resultados; "
                    + "reduza a página ou o tamanho da página."
            );

        return await buscaDeJogos.BuscarAsync(
            termo,
            (pagina - 1) * tamanho,
            tamanho,
            cancellationToken
        );
    }
}
