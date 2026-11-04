using System.Net;
using System.Net.Http.Json;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

// Cada escrita de jogo grava o evento de alteração no outbox, no mesmo commit do jogo. As queries
// filtram pelo id do jogo no corpo do evento — a tabela outbox_message não é truncada entre
// testes, então contagem global seria frágil na coleção em série.
public class JogosOutboxTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task PostDeveGravarUmaLinhaDeOutboxComOIdDoJogoCriado()
    {
        HttpClient admin = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());

        JogoResponse jogo = await CriarJogoAsync(admin, "Ori and the Blind Forest");

        (await ContarEventosAsync(jogo.Id)).Should().Be(1);
    }

    [Fact]
    public async Task PutDeveGravarUmaLinhaDeOutbox()
    {
        HttpClient admin = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        JogoResponse jogo = await CriarJogoAsync(admin, "Cuphead");

        HttpResponseMessage resposta = await admin.PutAsJsonAsync(
            $"/api/jogos/{jogo.Id}",
            new { titulo = "Cuphead: The Delicious Last Course", preco = 49.90m }
        );

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ContarEventosAsync(jogo.Id)).Should().Be(2);
    }

    [Fact]
    public async Task PatchDesativarDeveGravarUmaLinhaDeOutbox()
    {
        HttpClient admin = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        JogoResponse jogo = await CriarJogoAsync(admin, "Dead Cells");

        HttpResponseMessage resposta = await admin.PatchAsync(
            $"/api/jogos/{jogo.Id}/desativar",
            content: null
        );

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ContarEventosAsync(jogo.Id)).Should().Be(2);
    }

    [Fact]
    public async Task PutEPatchDeIdInexistenteNaoDevemGravarLinhaDeOutbox()
    {
        HttpClient admin = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        var inexistente = Guid.NewGuid();

        HttpResponseMessage put = await admin.PutAsJsonAsync(
            $"/api/jogos/{inexistente}",
            new { titulo = "Fantasma", preco = 10m }
        );
        HttpResponseMessage patch = await admin.PatchAsync(
            $"/api/jogos/{inexistente}/desativar",
            content: null
        );

        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ContarEventosAsync(inexistente)).Should().Be(0);
    }

    private static async Task<JogoResponse> CriarJogoAsync(HttpClient admin, string titulo)
    {
        HttpResponseMessage resposta = await admin.PostAsJsonAsync(
            "/api/jogos",
            new { titulo, preco = 39.90m }
        );
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await resposta.Content.ReadFromJsonAsync<JogoResponse>())!;
    }

    private async Task<int> ContarEventosAsync(Guid jogoId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        return await db
            .Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM outbox_message "
                    + "WHERE message_type LIKE '%JogoAlteradoEvent%' AND body LIKE {0}",
                $"%{jogoId}%"
            )
            .SingleAsync();
    }
}
