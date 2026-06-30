using System.Net;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
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
    public async Task LiveDeveRetornar200()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/health/live");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
