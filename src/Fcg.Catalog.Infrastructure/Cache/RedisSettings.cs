using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Fcg.Catalog.Infrastructure.Cache;

// Campos separados em vez de connection string única, no precedente do host do broker: o
// não-sensível cabe em ConfigMap e a senha fica em Secret, sem um valor só que misture os dois.
//
// Host é o gatilho do config-gate: ausente, a aplicação sobe e responde sem cache. As demais
// chaves só são lidas quando há host, para que um ambiente sem cache não precise declará-las.
public sealed class RedisSettings
{
    public const string ChaveHost = "Redis:Host";
    public const string ChavePort = "Redis:Port";
    public const string ChavePassword = "Redis:Password";

    private const int PortaPadrao = 6379;

    private RedisSettings(string host, int port, string? password)
    {
        Host = host;
        Port = port;
        Password = password;
    }

    public string Host { get; }

    public int Port { get; }

    public string? Password { get; }

    public bool Habilitado => Host.Length > 0;

    public static RedisSettings Ler(IConfiguration configuration)
    {
        string? host = configuration[ChaveHost];

        if (string.IsNullOrWhiteSpace(host))
        {
            return new RedisSettings(string.Empty, PortaPadrao, password: null);
        }

        string? password = configuration[ChavePassword];

        return new RedisSettings(
            host.Trim(),
            LerPorta(configuration),
            string.IsNullOrWhiteSpace(password) ? null : password
        );
    }

    // Porta presente e ilegível lança em vez de cair no default: silenciar faria o serviço
    // conectar noutra porta achando que respeitou a configuração injetada.
    private static int LerPorta(IConfiguration configuration)
    {
        string? porta = configuration[ChavePort];

        if (string.IsNullOrWhiteSpace(porta))
        {
            return PortaPadrao;
        }

        if (!int.TryParse(porta, CultureInfo.InvariantCulture, out int valor))
        {
            throw new InvalidOperationException($"{ChavePort} não é um número inteiro: '{porta}'.");
        }

        return valor;
    }
}
