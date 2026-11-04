using System.Net;
using System.Text.Json;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Busca;

// A fixture compartilhada não tem índice de busca: é ela que prova a forma desligada. A forma
// ligada com o cluster fora do ar vem de um host derivado apontado para uma porta fechada.
public class BuscaSaudeTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SemEndpointOAgregadoNaoDeveTerCheckDeBusca()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/health");

        (await NomesDosChecksAsync(resposta)).Should().NotContain("opensearch");
    }

    // Postgres e DynamoDB seguem no ar: a prontidão não pode depender da busca, e o agregado
    // informa o cluster fora do ar.
    [Fact]
    public async Task BuscaInalcancavelNaoDeveDerrubarAProntidao()
    {
        using WebApplicationFactory<Program> comBuscaForaDoAr = Factory.WithWebHostBuilder(
            builder => builder.UseSetting(OpenSearchSettings.ChaveEndpoint, "http://127.0.0.1:1")
        );

        HttpClient client = comBuscaForaDoAr.CreateClient();

        HttpResponseMessage pronto = await client.GetAsync("/health/ready");
        HttpResponseMessage agregado = await client.GetAsync("/health");

        pronto.StatusCode.Should().Be(HttpStatusCode.OK);

        using var corpo = JsonDocument.Parse(await agregado.Content.ReadAsStringAsync());
        JsonElement check = corpo
            .RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "opensearch");
        check.GetProperty("status").GetString().Should().Be("Unhealthy");
    }

    private static async Task<IReadOnlyList<string?>> NomesDosChecksAsync(
        HttpResponseMessage resposta
    )
    {
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());

        return
        [
            .. corpo
                .RootElement.GetProperty("checks")
                .EnumerateArray()
                .Select(c => c.GetProperty("name").GetString()),
        ];
    }
}
