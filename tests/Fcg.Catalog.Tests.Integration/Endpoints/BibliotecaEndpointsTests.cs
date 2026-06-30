using System.Net;
using System.Net.Http.Json;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

public class BibliotecaEndpointsTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ObterBibliotecaVaziaDeveRetornar200ComListaVazia()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"/api/biblioteca/{Guid.NewGuid()}");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        IReadOnlyList<ItemBibliotecaResponse>? itens = await resposta.Content.ReadFromJsonAsync<
            IReadOnlyList<ItemBibliotecaResponse>
        >();
        itens.Should().NotBeNull();
        itens!.Should().BeEmpty();
    }
}
