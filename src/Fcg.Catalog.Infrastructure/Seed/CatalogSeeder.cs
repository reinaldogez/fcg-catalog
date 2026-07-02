using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fcg.Catalog.Infrastructure.Seed;

// Reference data do catálogo inicial da plataforma. Idempotente por presença ("se vazio"): só
// popula quando a tabela está vazia. É um fluxo de bootstrap standalone (Job), então é dono do
// próprio commit — ao contrário do consumer, cujo commit é do harness do Inbox.
public class CatalogSeeder(CatalogDbContext contexto, ILogger<CatalogSeeder> logger)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await contexto.Jogos.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Catálogo já populado — seed ignorado.");
            return;
        }

        IReadOnlyList<SeedGameDto> semente = await CarregarSementeAsync(cancellationToken);

        foreach (SeedGameDto dto in semente)
        {
            // Kind=Utc explícito: a coluna é timestamptz (default Npgsql) e rejeita Unspecified.
            DateTime? dataLancamento = dto.DataLancamento is null
                ? null
                : DateTime.SpecifyKind(
                    DateTime.Parse(dto.DataLancamento, CultureInfo.InvariantCulture),
                    DateTimeKind.Utc
                );

            // Jogo.Criar() intocado: o factory gera o Guid internamente e valida os VOs.
            var jogo = Jogo.Criar(
                Titulo.Criar(dto.Titulo),
                Preco.Criar(dto.Preco),
                dto.Descricao,
                dto.Desenvolvedora,
                dataLancamento
            );

            contexto.Jogos.Add(jogo);
        }

        // Commit único do bootstrap, via a abstração de UnitOfWork do próprio contexto.
        await contexto.SalvarAlteracoesAsync(cancellationToken);
        logger.LogInformation("Catálogo semeado com {Quantidade} jogos.", semente.Count);
    }

    // Catálogo-semente lido do recurso embarcado. Nome localizado por sufixo — robusto ao
    // namespace/hífen que o MSBuild embute no manifest.
    private static async Task<IReadOnlyList<SeedGameDto>> CarregarSementeAsync(
        CancellationToken cancellationToken
    )
    {
        Assembly assembly = typeof(CatalogSeeder).Assembly;
        string recurso = assembly
            .GetManifestResourceNames()
            .Single(nome => nome.EndsWith("seed-games.json", StringComparison.Ordinal));

        await using Stream stream = assembly.GetManifestResourceStream(recurso)!;

        List<SeedGameDto>? semente = await JsonSerializer.DeserializeAsync<List<SeedGameDto>>(
            stream,
            s_jsonOptions,
            cancellationToken
        );

        return semente ?? [];
    }

    private sealed record SeedGameDto(
        string Titulo,
        decimal Preco,
        string? Descricao,
        string? Desenvolvedora,
        string? DataLancamento
    );
}
