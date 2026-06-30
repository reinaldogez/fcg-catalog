using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Application.UseCases.Jogos;

public class ListarJogosUseCase(IJogoRepository jogoRepository)
{
    private const int TamanhoMaximo = 100;

    public async Task<IReadOnlyList<JogoResponse>> ExecutarAsync(
        ListarJogosRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Saneamento da paginação — evita página/tamanho inválidos chegarem ao repositório.
        int pagina = request.Pagina < 1 ? 1 : request.Pagina;
        int tamanho = Math.Clamp(request.TamanhoPagina, 1, TamanhoMaximo);

        IReadOnlyList<Jogo> jogos = await jogoRepository.ListarAsync(
            pagina,
            tamanho,
            cancellationToken
        );

        return [.. jogos.Select(JogoResponse.De)];
    }
}
