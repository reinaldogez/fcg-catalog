using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.Services;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Infrastructure.DynamoDb;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using OpenSearch.Net;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Busca;

// Fora das coleções: a composição da busca é decidida sem containers. Os dois primeiros testes
// sobem um host próprio só para provar que o fail-fast derruba o boot; os demais compõem a
// extensão isolada, onde nada conecta.
public class BuscaComposicaoTests
{
    [Fact]
    public void StartupComEndpointInvalidoDeveFalharNaComposicao()
    {
        using WebApplicationFactory<Program> factory = CriarHost(
            endpoint: "busca.exemplo.local",
            useSigV4: null
        );

        // CreateClient dispara a construção do host, e com ela a composição da Infrastructure.
        Action start = () => factory.CreateClient();

        start
            .Should()
            .Throw<Exception>()
            .Which.ToString()
            .Should()
            .Contain(OpenSearchSettings.ChaveEndpoint);
    }

    [Fact]
    public void StartupComAssinaturaSobreHttpDeveFalharNaComposicao()
    {
        using WebApplicationFactory<Program> factory = CriarHost(
            endpoint: "http://busca.exemplo.local",
            useSigV4: "true"
        );

        Action start = () => factory.CreateClient();

        start
            .Should()
            .Throw<Exception>()
            .Which.ToString()
            .Should()
            .Contain(OpenSearchSettings.ChaveUseSigV4);
    }

    [Fact]
    public async Task SemEndpointNaoDeveRegistrarClienteNemCheck()
    {
        await using ServiceProvider provider = Compor([]);

        provider.GetService<IOpenSearchClient>().Should().BeNull();
        NomesDosChecks(provider).Should().NotContain("opensearch");
    }

    [Fact]
    public async Task ComEndpointDeveRegistrarCheckForaDaProntidao()
    {
        await using ServiceProvider provider = Compor(
            new Dictionary<string, string?>
            {
                [OpenSearchSettings.ChaveEndpoint] = "http://localhost:9200",
            }
        );

        HealthCheckRegistration registro = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == "opensearch");

        registro.Tags.Should().BeEmpty();
    }

    // Timeout abaixo do teto do gateway, nó único e nenhuma repetição no cliente: o 503 da busca
    // sai do serviço, e repetir escrita é papel do consumer.
    [Fact]
    public async Task ComEndpointOClienteDeveTerNoUnicoTimeoutDeDezSegundosESemRetry()
    {
        await using ServiceProvider provider = Compor(
            new Dictionary<string, string?>
            {
                [OpenSearchSettings.ChaveEndpoint] = "http://localhost:9200",
            }
        );

        IConnectionSettingsValues configuracao = provider
            .GetRequiredService<IOpenSearchClient>()
            .ConnectionSettings;

        configuracao.ConnectionPool.Should().BeOfType<SingleNodeConnectionPool>();
        configuracao.ConnectionPool.Nodes.Should().ContainSingle();
        configuracao.RequestTimeout.Should().Be(TimeSpan.FromSeconds(10));
        configuracao.MaxRetries.Should().Be(0);
    }

    private static WebApplicationFactory<Program> CriarHost(string endpoint, string? useSigV4) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Passa os demais fail-fast; o alvo aqui é só a configuração da busca.
            builder.UseSetting(
                "ConnectionStrings:Catalog",
                "Host=localhost;Database=fail;Username=u;Password=p"
            );
            builder.UseSetting("Jwt:JwksUri", "https://identity.local/.well-known/jwks.json");
            builder.UseSetting("Jwt:Issuer", "https://identity.local");
            builder.UseSetting("Jwt:Audience", "fcg");
            builder.UseSetting(DynamoDbSettings.ChaveTableName, "biblioteca");
            builder.UseSetting(DynamoDbSettings.ChaveServiceUrl, "http://localhost:8000");
            builder.UseSetting(OpenSearchSettings.ChaveEndpoint, endpoint);

            if (useSigV4 is not null)
            {
                builder.UseSetting(OpenSearchSettings.ChaveUseSigV4, useSigV4);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IPedidoDomainService, PedidoDomainService>();
                services.AddSingleton<IPublishEndpoint, PublishEndpointNoop>();
            });
        });

    private static ServiceProvider Compor(Dictionary<string, string?> entradas)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCatalogSearch(
            new ConfigurationBuilder().AddInMemoryCollection(entradas).Build()
        );

        return services.BuildServiceProvider();
    }

    private static IEnumerable<string> NomesDosChecks(ServiceProvider provider) =>
        provider
            .GetService<IOptions<HealthCheckServiceOptions>>()
            ?.Value.Registrations.Select(r => r.Name)
        ?? [];
}
