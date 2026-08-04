namespace Fcg.Catalog.Application.DTOs;

public record AtualizarJogoRequest(
    string Titulo,
    decimal Preco,
    string? Descricao = null,
    string? Desenvolvedora = null,
    DateTime? DataLancamento = null
)
{
    private readonly DateTime? _dataLancamento = InstanteDeEntrada.ParaUtc(DataLancamento);

    // Normaliza na fronteira de entrada: quem lê a propriedade recebe um instante já em UTC, e
    // nenhuma camada abaixo precisa conhecer o problema de fuso.
    public DateTime? DataLancamento
    {
        get => _dataLancamento;
        init => _dataLancamento = InstanteDeEntrada.ParaUtc(value);
    }
}
