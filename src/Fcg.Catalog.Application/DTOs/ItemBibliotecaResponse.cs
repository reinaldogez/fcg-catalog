namespace Fcg.Catalog.Application.DTOs;

// Item da biblioteca como a API o devolve. Não carrega o identificador técnico do write model:
// aquele é chave de uma tabela relacional, é inacessível ao caminho de evento que alimenta esta
// leitura, e expô-lo prenderia o contrato ao armazenamento. Nome do jogo e preço são o snapshot
// da compra, e são o que dispensa uma segunda chamada ao catálogo para montar a tela.
public record ItemBibliotecaResponse(
    Guid UsuarioId,
    Guid JogoId,
    Guid PedidoId,
    string NomeJogo,
    decimal Preco,
    DateTime AdquiridoEm
);
