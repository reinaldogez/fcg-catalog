using Microsoft.Extensions.Configuration;

namespace Fcg.Catalog.Infrastructure.Busca;

// Leitura isolada das chaves do índice de busca, para que o config-gate e o fail-fast sejam
// asseríveis sem construir cliente algum.
//
// O endpoint é o gatilho: ausente, a aplicação sobe sem busca e o endpoint de busca responde
// indisponível. A assinatura só é lida quando há endpoint, para que um ambiente sem busca não
// precise declará-la. Credencial e região não viram chave nossa — vêm da cadeia padrão do SDK.
public sealed class OpenSearchSettings
{
    public const string ChaveEndpoint = "OpenSearch:Endpoint";
    public const string ChaveUseSigV4 = "OpenSearch:UseSigV4";

    private OpenSearchSettings(Uri? endpoint, bool useSigV4)
    {
        Endpoint = endpoint;
        UseSigV4 = useSigV4;
    }

    public Uri? Endpoint { get; }

    public bool UseSigV4 { get; }

    public bool Habilitado => Endpoint is not null;

    // Fail-fast na composição: um endpoint malformado que seguisse em frente só apareceria na
    // primeira busca, já servindo tráfego, e com uma mensagem que não aponta a chave.
    public static OpenSearchSettings Ler(IConfiguration configuration)
    {
        string? valor = configuration[ChaveEndpoint];

        if (string.IsNullOrWhiteSpace(valor))
        {
            return new OpenSearchSettings(endpoint: null, useSigV4: false);
        }

        if (
            !Uri.TryCreate(valor.Trim(), UriKind.Absolute, out Uri? endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
        )
        {
            throw new InvalidOperationException(
                $"{ChaveEndpoint} não é uma URL http(s) absoluta: '{valor}'."
            );
        }

        bool useSigV4 = LerUseSigV4(configuration);

        // A assinatura cobre o conteúdo da requisição, não o canal: sem TLS a credencial
        // temporária do pod e o próprio tráfego iriam em claro.
        if (useSigV4 && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{ChaveUseSigV4} exige {ChaveEndpoint} com https: '{valor}'."
            );
        }

        return new OpenSearchSettings(endpoint, useSigV4);
    }

    // Valor presente e ilegível lança em vez de cair no default: silenciar faria o serviço falar
    // sem assinatura com um domínio que só aceita requisição assinada, achando que respeitou a
    // configuração injetada.
    private static bool LerUseSigV4(IConfiguration configuration)
    {
        string? valor = configuration[ChaveUseSigV4];

        if (string.IsNullOrWhiteSpace(valor))
        {
            return false;
        }

        if (!bool.TryParse(valor.Trim(), out bool useSigV4))
        {
            throw new InvalidOperationException($"{ChaveUseSigV4} não é booleano: '{valor}'.");
        }

        return useSigV4;
    }
}
