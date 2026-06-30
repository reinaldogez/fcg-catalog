using Fcg.Catalog.Domain.Exceptions;

namespace Fcg.Catalog.Domain.ValueObjects;

// Título do jogo: não-vazio, com trim, até 200 caracteres. Sem unicidade.
public record Titulo
{
    public const int ComprimentoMaximo = 200;

    private Titulo(string valor) => Valor = valor;

    public string Valor { get; }

    public static Titulo Criar(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new DomainException("O título não pode ser vazio.");

        string normalizado = valor.Trim();

        if (normalizado.Length > ComprimentoMaximo)
            throw new DomainException($"O título não pode exceder {ComprimentoMaximo} caracteres.");

        return new Titulo(normalizado);
    }

    // Materialização (leitura do EF) — sem validação.
    public static Titulo Reconstituir(string valor) => new(valor);
}
