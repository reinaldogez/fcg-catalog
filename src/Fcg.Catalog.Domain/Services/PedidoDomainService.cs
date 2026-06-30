using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Exceptions;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Domain.Services;

// Encapsula as invariantes de criação de pedido que consultam o repositório.
// Decide e cria — NÃO persiste (o commit é do use case, no fim).
public class PedidoDomainService(
    IJogoRepository jogoRepository,
    IItemBibliotecaRepository itemBibliotecaRepository,
    IPedidoRepository pedidoRepository
) : IPedidoDomainService
{
    public async Task<Pedido> RegistrarAsync(
        Guid usuarioId,
        Guid jogoId,
        CancellationToken cancellationToken = default
    )
    {
        // Jogo precisa existir e estar ativo — validação local, mesmo banco.
        Jogo? jogo = await jogoRepository.ObterPorIdAsync(jogoId, cancellationToken);
        if (jogo is null || !jogo.Ativo)
            throw new DomainException("Jogo não encontrado ou indisponível.");

        // Usuário não pode pedir um jogo que já possui.
        if (await itemBibliotecaRepository.ExisteAsync(usuarioId, jogoId, cancellationToken))
            throw new DomainConflictException("O usuário já possui este jogo.");

        // No máximo um pedido pendente por (usuário, jogo) — evita cobrança dupla.
        // Pedidos rejeitados são terminais (não-pendentes), então um retry é permitido.
        if (await pedidoRepository.ExistePedidoPendenteAsync(usuarioId, jogoId, cancellationToken))
            throw new DomainConflictException("Já existe um pedido pendente para este jogo.");

        return Pedido.Criar(usuarioId, jogoId, jogo.Preco);
    }
}
