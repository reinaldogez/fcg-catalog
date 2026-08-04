using System.Net;
using Fcg.Catalog.Infrastructure.DynamoDb;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

public class HealthEndpointsTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ReadyDeveRetornar200ComPostgresNoAr()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/health/ready");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyDeveReportarNaoProntoComDynamoDbInalcancavel()
    {
        // Host derivado apontando o read model para uma porta fechada; Postgres segue no ar, então
        // o não-pronto só pode vir do check do DynamoDB.
        using WebApplicationFactory<Program> comDynamoForaDoAr = Factory.WithWebHostBuilder(
            builder => builder.UseSetting(DynamoDbSettings.ChaveServiceUrl, "http://localhost:1")
        );

        HttpClient client = comDynamoForaDoAr.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/health/ready");

        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("\"dynamodb\"");
    }

    [Fact]
    public async Task LiveDeveRetornar200()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/health/live");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
