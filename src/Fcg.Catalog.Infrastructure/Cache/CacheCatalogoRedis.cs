using System.Globalization;
using System.Text.Json;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Infrastructure.Cache;

// Adaptador do cache do catálogo. Concentra as quatro responsabilidades que a porta esconde:
// composição de chave, versionamento da listagem, prazo de validade e serialização — mais a
// absorção de exceção que faz o cache falhar aberto.
//
// Falha aberta: leitura que quebra vira miss e escrita que quebra vira aviso, porque cache é
// otimização e perder o Redis custa latência, não corretude. É o oposto do modelo de leitura da
// biblioteca, que falha fechado por ser a própria resposta.
public sealed class CacheCatalogoRedis(IDistributedCache cache, ILogger<CacheCatalogoRedis> logger)
    : ICacheCatalogo
{
    public const string ChaveDaVersao = "jogos:versao";

    public const string PrefixoDaListagem = "jogos:lista";

    public const string PrefixoDoDetalhe = "jogos:";

    // Rede de segurança apenas: a coerência vem da invalidação nos comandos de escrita. O prazo
    // limita por quanto tempo as páginas de versões vencidas ocupam memória do Redis.
    private static readonly DistributedCacheEntryOptions s_prazoDeValidade = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    private const long VersaoInicial = 0;

    public async Task<IReadOnlyList<JogoResponse>?> ObterListagemAsync(
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default
    )
    {
        long? versao = await LerVersaoAsync(cancellationToken);

        if (versao is null)
        {
            return null;
        }

        return await LerAsync<IReadOnlyList<JogoResponse>>(
            ChaveDaListagem(versao.Value, pagina, tamanhoPagina),
            cancellationToken
        );
    }

    public async Task GravarListagemAsync(
        int pagina,
        int tamanhoPagina,
        IReadOnlyList<JogoResponse> jogos,
        CancellationToken cancellationToken = default
    )
    {
        long? versao = await LerVersaoAsync(cancellationToken);

        if (versao is null)
        {
            return;
        }

        await GravarAsync(
            ChaveDaListagem(versao.Value, pagina, tamanhoPagina),
            jogos,
            cancellationToken
        );
    }

    public Task<JogoResponse?> ObterDetalheAsync(
        Guid jogoId,
        CancellationToken cancellationToken = default
    ) => LerAsync<JogoResponse>(ChaveDoDetalhe(jogoId), cancellationToken);

    public Task GravarDetalheAsync(
        JogoResponse jogo,
        CancellationToken cancellationToken = default
    ) => GravarAsync(ChaveDoDetalhe(jogo.Id), jogo, cancellationToken);

    // Invalidação da listagem por incremento de versão: são N chaves de página, ninguém guarda
    // quais foram criadas, e a abstração de cache distribuído só opera por chave exata. Subir a
    // versão torna as chaves antigas inalcançáveis de uma vez, e elas morrem pelo prazo.
    public async Task InvalidarListagemAsync(CancellationToken cancellationToken = default)
    {
        long? versao = await LerVersaoAsync(cancellationToken);

        if (versao is null)
        {
            return;
        }

        try
        {
            // A versão é gravada sem prazo de validade de propósito: se ela expirasse, o contador
            // voltaria ao início e as páginas antigas ainda vivas voltariam a ser alcançáveis.
            //
            // Ler e gravar não é atômico, e dois comandos concorrentes podem gravar o mesmo
            // número. O desfecho é benigno: ambos queriam sair da versão corrente, e ambos saem.
            await cache.SetStringAsync(
                ChaveDaVersao,
                (versao.Value + 1).ToString(CultureInfo.InvariantCulture),
                cancellationToken
            );
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            logger.LogWarning(
                excecao,
                "Falha ao incrementar a versão da listagem no cache; as páginas gravadas seguem alcançáveis até vencerem."
            );
        }
    }

    public async Task InvalidarDetalheAsync(
        Guid jogoId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await cache.RemoveAsync(ChaveDoDetalhe(jogoId), cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            logger.LogWarning(excecao, "Falha ao remover o detalhe {JogoId} do cache.", jogoId);
        }
    }

    private static string ChaveDaListagem(long versao, int pagina, int tamanhoPagina) =>
        $"{PrefixoDaListagem}:v{versao.ToString(CultureInfo.InvariantCulture)}:p{pagina.ToString(CultureInfo.InvariantCulture)}:t{tamanhoPagina.ToString(CultureInfo.InvariantCulture)}";

    // A página entra na chave: com chave única, quem pedisse a página 3 receberia o conteúdo da
    // página 1 — resposta errada, não velha.
    private static string ChaveDoDetalhe(Guid jogoId) => PrefixoDoDetalhe + jogoId;

    // Nulo aqui significa cache indisponível, e não versão ausente: chave nunca gravada é a
    // versão inicial, e o chamador segue compondo a chave normalmente.
    private async Task<long?> LerVersaoAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? valor = await cache.GetStringAsync(ChaveDaVersao, cancellationToken);

            return long.TryParse(valor, CultureInfo.InvariantCulture, out long versao)
                ? versao
                : VersaoInicial;
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            logger.LogWarning(excecao, "Falha ao ler a versão da listagem no cache.");
            return null;
        }
    }

    private async Task<T?> LerAsync<T>(string chave, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            string? conteudo = await cache.GetStringAsync(chave, cancellationToken);

            return conteudo is null ? null : JsonSerializer.Deserialize<T>(conteudo);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            logger.LogWarning(excecao, "Falha ao ler a chave {Chave} do cache.", chave);
            return null;
        }
    }

    private async Task GravarAsync<T>(string chave, T valor, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(
                chave,
                JsonSerializer.Serialize(valor),
                s_prazoDeValidade,
                cancellationToken
            );
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            logger.LogWarning(excecao, "Falha ao gravar a chave {Chave} no cache.", chave);
        }
    }
}
