using System.Net;
using System.Net.Http.Json;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

public class AutorizacaoTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SemTokenEmEndpointProtegidoDeveRetornar401()
    {
        HttpClient client = Factory.CreateClient();

        HttpResponseMessage resposta = await client.GetAsync("/api/jogos");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenValidoEmEndpointAutenticadoDeveRetornar200()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(Guid.NewGuid())
        );

        HttpResponseMessage resposta = await client.GetAsync("/api/jogos");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NaoAdminEmCriarJogoDeveRetornar403()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(Guid.NewGuid())
        );

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            "/api/jogos",
            new { titulo = "Hades", preco = 45m }
        );

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminEmCriarJogoDeveRetornar201()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            "/api/jogos",
            new { titulo = "Hades", preco = 45m }
        );

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Biblioteca_ProprioUsuarioDeveRetornar200()
    {
        var usuarioId = Guid.NewGuid();
        HttpClient client = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(usuarioId)
        );

        HttpResponseMessage resposta = await client.GetAsync($"/api/biblioteca/{usuarioId}");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Biblioteca_OutroUsuarioDeveRetornar403()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(Guid.NewGuid())
        );

        HttpResponseMessage resposta = await client.GetAsync($"/api/biblioteca/{Guid.NewGuid()}");

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pedido_DonoDeveRetornar200_NaoDonoDeveRetornar403()
    {
        var dono = Guid.NewGuid();
        Guid jogoId = await CriarJogoAsync();

        HttpClient clienteDono = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(dono)
        );
        PedidoResponse pedido = await CriarPedidoAsync(clienteDono, jogoId);

        HttpResponseMessage comoDono = await clienteDono.GetAsync($"/api/pedidos/{pedido.Id}");
        comoDono.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpClient clienteOutro = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(Guid.NewGuid())
        );
        HttpResponseMessage comoOutro = await clienteOutro.GetAsync($"/api/pedidos/{pedido.Id}");
        comoOutro.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pedido_InexistenteDeveRetornar404_AntesDaChecagemDeOwnership()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(Guid.NewGuid())
        );

        HttpResponseMessage resposta = await client.GetAsync($"/api/pedidos/{Guid.NewGuid()}");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CriarPedido_UsaSubDoTokenComoUsuarioId()
    {
        var usuarioId = Guid.NewGuid();
        Guid jogoId = await CriarJogoAsync();

        HttpClient client = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenUsuario(usuarioId)
        );
        PedidoResponse pedido = await CriarPedidoAsync(client, jogoId);

        pedido.UsuarioId.Should().Be(usuarioId);
    }

    private async Task<Guid> CriarJogoAsync()
    {
        HttpClient admin = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        HttpResponseMessage criacao = await admin.PostAsJsonAsync(
            "/api/jogos",
            new { titulo = "Celeste", preco = 39.90m }
        );
        criacao.StatusCode.Should().Be(HttpStatusCode.Created);
        JogoResponse jogo = (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!;
        return jogo.Id;
    }

    private static async Task<PedidoResponse> CriarPedidoAsync(HttpClient client, Guid jogoId)
    {
        HttpResponseMessage criacao = await client.PostAsJsonAsync("/api/pedidos", new { jogoId });
        criacao.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await criacao.Content.ReadFromJsonAsync<PedidoResponse>())!;
    }
}
