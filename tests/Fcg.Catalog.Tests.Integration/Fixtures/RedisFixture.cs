using System.Globalization;
using System.Net;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Fixture própria do cache: a fixture compartilhada da suíte permanece sem Redis, e só as classes
// que exercitam o adaptador pagam o container.
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();

    private ConnectionMultiplexer? _conexao;

    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; }

    // Conexão direta, só para inspecionar o que o adaptador gravou — chave literal, prazo restante
    // e sobrevivência da chave que ficou inalcançável. Nenhum caminho de produção passa por aqui.
    public IDatabase Inspecao =>
        (
            _conexao ?? throw new InvalidOperationException("Fixture não inicializada.")
        ).GetDatabase();

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();

        string[] endereco = _redis.GetConnectionString().Split(':', 2);
        Host = endereco[0];
        Port = int.Parse(endereco[1], CultureInfo.InvariantCulture);

        // AllowAdmin é o que libera o flush entre testes; a conexão do adaptador não o usa.
        _conexao = await ConnectionMultiplexer.ConnectAsync(
            new ConfigurationOptions { EndPoints = { { Host, Port } }, AllowAdmin = true }
        );
    }

    // O Redis não tem truncate por prefixo, e as chaves de versão sobrevivem ao prazo: sem o
    // flush, a versão herdada de um teste deslocaria as chaves esperadas pelo seguinte.
    public async Task LimparAsync()
    {
        ConnectionMultiplexer conexao =
            _conexao ?? throw new InvalidOperationException("Fixture não inicializada.");

        foreach (EndPoint endpoint in conexao.GetEndPoints())
        {
            await conexao.GetServer(endpoint).FlushDatabaseAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
        }

        await _redis.DisposeAsync();
    }
}
