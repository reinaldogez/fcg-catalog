using Fcg.Catalog.Domain.ValueObjects;

namespace Fcg.Catalog.Domain.Entities;

public class Jogo
{
    // EF materializa por aqui.
    private Jogo() { }

    public Guid Id { get; private set; }
    public Titulo Titulo { get; private set; } = null!;
    public string? Descricao { get; private set; }
    public Preco Preco { get; private set; } = null!;
    public string? Desenvolvedora { get; private set; }
    public DateTime? DataLancamento { get; private set; }
    public bool Ativo { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    public static Jogo Criar(
        Titulo titulo,
        Preco preco,
        string? descricao = null,
        string? desenvolvedora = null,
        DateTime? dataLancamento = null
    )
    {
        DateTime agora = DateTime.UtcNow;

        return new Jogo
        {
            Id = Guid.NewGuid(),
            Titulo = titulo,
            Preco = preco,
            Descricao = descricao,
            Desenvolvedora = desenvolvedora,
            DataLancamento = dataLancamento,
            Ativo = true,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };
    }

    public void Atualizar(
        Titulo titulo,
        Preco preco,
        string? descricao = null,
        string? desenvolvedora = null,
        DateTime? dataLancamento = null
    )
    {
        Titulo = titulo;
        Preco = preco;
        Descricao = descricao;
        Desenvolvedora = desenvolvedora;
        DataLancamento = dataLancamento;
        AtualizadoEm = DateTime.UtcNow;
    }

    // Soft delete: apenas vira a flag; o jogo não some de bibliotecas existentes.
    public void Desativar()
    {
        Ativo = false;
        AtualizadoEm = DateTime.UtcNow;
    }
}
