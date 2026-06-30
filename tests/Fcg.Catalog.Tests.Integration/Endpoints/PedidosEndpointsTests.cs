using System.Net;
using System.Net.Http.Json;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

public class PedidosEndpointsTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CriarPedidoDeveRetornar202()
    {
        HttpClient client = Factory.CreateClient();
        var novoJogo = new { titulo = "Stardew Valley", preco = 24.90m };
        HttpResponseMessage criacaoJogo = await client.PostAsJsonAsync("/api/jogos", novoJogo);
        JogoResponse jogo = (await criacaoJogo.Content.ReadFromJsonAsync<JogoResponse>())!;

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            "/api/pedidos",
            new { jogoId = jogo.Id }
        );

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ObterPedidoInexistenteDeveRetornar404()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync($"/api/pedidos/{Guid.NewGuid()}");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
