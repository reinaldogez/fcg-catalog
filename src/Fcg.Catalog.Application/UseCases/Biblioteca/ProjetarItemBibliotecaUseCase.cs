using Fcg.Catalog.Application.Abstractions;

namespace Fcg.Catalog.Application.UseCases.Biblioteca;

// Materializa a compra no modelo de leitura. Recebe primitivos e não o tipo do evento: a camada de
// aplicação não conhece o contrato de mensageria, e extrair os campos é papel da casca do consumer.
// Não comita nem abre transação — a escrita no modelo de leitura é a operação inteira, e ela vive
// fora do banco relacional.
public class ProjetarItemBibliotecaUseCase(IProjecaoBiblioteca projecaoBiblioteca)
{
    public Task ExecutarAsync(
        Guid usuarioId,
        Guid jogoId,
        Guid pedidoId,
        string nomeJogo,
        decimal preco,
        DateTime adquiridoEm,
        CancellationToken cancellationToken = default
    ) =>
        projecaoBiblioteca.ProjetarAsync(
            new ItemBibliotecaProjetado(usuarioId, jogoId, pedidoId, nomeJogo, preco, adquiridoEm),
            cancellationToken
        );
}
