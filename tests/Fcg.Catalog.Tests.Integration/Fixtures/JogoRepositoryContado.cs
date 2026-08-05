using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Contador compartilhado pela fixture: o repositório é scoped e uma requisição por escopo, então
// o número só é observável fora dele.
public sealed class ContadorDeConsultasDeJogo
{
    private int _listagens;
    private int _obtencoesPorId;

    public int Listagens => Volatile.Read(ref _listagens);

    public int ObtencoesPorId => Volatile.Read(ref _obtencoesPorId);

    public void Zerar()
    {
        Interlocked.Exchange(ref _listagens, 0);
        Interlocked.Exchange(ref _obtencoesPorId, 0);
    }

    internal void RegistrarListagem() => Interlocked.Increment(ref _listagens);

    internal void RegistrarObtencaoPorId() => Interlocked.Increment(ref _obtencoesPorId);
}

// Decorator sobre o repositório real, para contar quantas vezes a fonte da verdade é consultada
// atrás do cache-aside. Não altera comportamento: delega tudo e só conta as duas leituras.
//
// É o repositório que se conta, e não o comando emitido ao banco: contagem no nível do driver
// somaria consulta de qualquer outro ponto da requisição, e um falso vermelho ali é caro de
// diagnosticar.
public sealed class JogoRepositoryContado(
    IJogoRepository interno,
    ContadorDeConsultasDeJogo contador
) : IJogoRepository
{
    public Task<Jogo?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        contador.RegistrarObtencaoPorId();
        return interno.ObterPorIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<Jogo>> ListarAsync(
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default
    )
    {
        contador.RegistrarListagem();
        return interno.ListarAsync(pagina, tamanhoPagina, cancellationToken);
    }

    public Task AdicionarAsync(Jogo jogo, CancellationToken cancellationToken = default) =>
        interno.AdicionarAsync(jogo, cancellationToken);

    public void Atualizar(Jogo jogo) => interno.Atualizar(jogo);
}
