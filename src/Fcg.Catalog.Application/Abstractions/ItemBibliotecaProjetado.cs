namespace Fcg.Catalog.Application.Abstractions;

// Snapshot desnormalizado da compra, na forma que o modelo de leitura guarda e devolve. Título e
// preço são o que valia no momento do pedido, não o que o catálogo diz hoje: é isso que dispensa
// a consulta ao catálogo na hora de servir a biblioteca.
//
// Não carrega identificador de pagamento nem o identificador técnico do write model. O primeiro é
// dado de outro contexto e não sobreviveria a uma reconstrução; o segundo é inacessível ao caminho
// de evento, e os dois juntos destruiriam a convergência entre os dois caminhos de escrita.
public sealed record ItemBibliotecaProjetado(
    Guid UsuarioId,
    Guid JogoId,
    Guid PedidoId,
    string NomeJogo,
    decimal Preco,
    DateTime AdquiridoEm
);
