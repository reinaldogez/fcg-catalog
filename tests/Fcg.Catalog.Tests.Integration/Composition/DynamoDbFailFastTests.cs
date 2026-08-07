using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.Services;
using Fcg.Catalog.Infrastructure.DynamoDb;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Composition;

// Fora da coleção de Integration: sobe um host próprio (sem containers) só para provar o
// fail-fast do nome da tabela do read model.
public class DynamoDbFailFastTests
{
    [Fact]
    public void StartupSemNomeDaTabelaDeveFalharNaComposicao()
    {
        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                // Passa os demais fail-fast; o alvo aqui é só a ausência do nome da tabela.
                builder.UseSetting(
                    "ConnectionStrings:Catalog",
                    "Host=localhost;Database=fail;Username=u;Password=p"
                );
                builder.UseSetting("Jwt:JwksUri", "https://identity.local/.well-known/jwks.json");
                builder.UseSetting("Jwt:Issuer", "https://identity.local");
                builder.UseSetting("Jwt:Audience", "fcg");
                builder.UseSetting(DynamoDbSettings.ChaveTableName, "");

                builder.ConfigureTestServices(services =>
                {
                    services.AddScoped<IPedidoDomainService, PedidoDomainService>();
                    services.AddSingleton<IPublishEndpoint, PublishEndpointNoop>();
                });
            });

        // CreateClient dispara a construção do host, e com ela a composição da Infrastructure.
        Action start = () => factory.CreateClient();

        // A exceção pode vir agregada pelo host; basta que a falha aponte a chave ausente.
        start
            .Should()
            .Throw<Exception>()
            .Which.ToString()
            .Should()
            .Contain(DynamoDbSettings.ChaveTableName);
    }

    // A decisão de endpoint é asserível sem construir cliente: ausência de ServiceUrl é a forma da
    // nuvem, onde região e credencial vêm do ambiente do pod. A composição-raiz não exercita este
    // ramo, porque supre ServiceUrl para não depender de ambiente AWS no runner.
    [Fact]
    public void ServiceUrlAusenteDeveIndicarRamoDeNuvem()
    {
        DynamoDbSettings settings = LerSettings(serviceUrl: null);

        settings.UsaEndpointLocal.Should().BeFalse();
        settings.ServiceUrl.Should().BeNull();
    }

    [Fact]
    public void ServiceUrlPresenteDeveIndicarRamoLocal()
    {
        DynamoDbSettings settings = LerSettings(serviceUrl: "http://dynamodb-local:8000");

        settings.UsaEndpointLocal.Should().BeTrue();
        settings.ServiceUrl.Should().Be("http://dynamodb-local:8000");
    }

    // Valor em branco é a forma que o ConfigMap da nuvem assume, e precisa contar como ausência.
    [Fact]
    public void ServiceUrlEmBrancoDeveIndicarRamoDeNuvem()
    {
        DynamoDbSettings settings = LerSettings(serviceUrl: "   ");

        settings.UsaEndpointLocal.Should().BeFalse();
        settings.ServiceUrl.Should().BeNull();
    }

    private static DynamoDbSettings LerSettings(string? serviceUrl)
    {
        Dictionary<string, string?> entradas = new()
        {
            [DynamoDbSettings.ChaveTableName] = "biblioteca",
            [DynamoDbSettings.ChaveServiceUrl] = serviceUrl,
        };

        return DynamoDbSettings.Ler(
            new ConfigurationBuilder().AddInMemoryCollection(entradas).Build()
        );
    }
}
