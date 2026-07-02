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

// Prova de atomicidade da Outbox: o evento e o Pedido caem no mesmo commit (nenhum
// dual-write). As queries filtram pelo GUID no corpo do evento — a tabela outbox_message
// não é truncada entre testes, então contagem global seria frágil na coleção em série.
public class CriarPedidoOutboxTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task PostFelizGravaPedidoEUmaLinhaDeOutboxNoMesmoCommit()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(
            JwtTestTokens.TokenAdmin(email: "ana@fcg.test", nome: "Ana")
        );
        var novoJogo = new { titulo = "Hollow Knight", preco = 4500.00m };
        HttpResponseMessage criacaoJogo = await client.PostAsJsonAsync("/api/jogos", novoJogo);
        JogoResponse jogo = (await criacaoJogo.Content.ReadFromJsonAsync<JogoResponse>())!;

        HttpResponseMessage resposta = await client.PostAsJsonAsync(
            "/api/pedidos",
            new { jogoId = jogo.Id }
        );

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        PedidoResponse pedido = (await resposta.Content.ReadFromJsonAsync<PedidoResponse>())!;

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // O Pedido foi persistido.
        int pedidos = await db
            .Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pedidos WHERE id = {0}",
                pedido.Id
            )
            .SingleAsync();
        pedidos.Should().Be(1);

        // Exatamente uma linha de Outbox para este pedido — commitada junto com ele.
        List<string> corpos = await db
            .Database.SqlQueryRaw<string>(
                "SELECT body AS \"Value\" FROM outbox_message WHERE body LIKE {0}",
                $"%{pedido.Id}%"
            )
            .ToListAsync();
        corpos.Should().ContainSingle();

        // O fat event carrega o título do jogo (GameName nasce aqui), o GameId e o preço snapshot.
        corpos[0].Should().Contain("Hollow Knight");
        corpos[0].Should().Contain(jogo.Id.ToString());
        corpos[0].Should().Contain("4500");
    }

    [Fact]
    public async Task PostDuplicadoPendenteFazRollbackDePedidoEDeOutbox()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        var novoJogo = new { titulo = "Celeste", preco = 75.00m };
        HttpResponseMessage criacaoJogo = await client.PostAsJsonAsync("/api/jogos", novoJogo);
        JogoResponse jogo = (await criacaoJogo.Content.ReadFromJsonAsync<JogoResponse>())!;

        // Primeiro pedido: aceito (fica pendente).
        HttpResponseMessage primeiro = await client.PostAsJsonAsync(
            "/api/pedidos",
            new { jogoId = jogo.Id }
        );
        primeiro.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Segundo pedido para o mesmo (usuário, jogo): barrado pela invariante → 409.
        HttpResponseMessage segundo = await client.PostAsJsonAsync(
            "/api/pedidos",
            new { jogoId = jogo.Id }
        );
        segundo.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // O rollback do segundo cobre pedido E outbox: nada novo de nenhum dos dois lados.
        int pedidos = await db
            .Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pedidos WHERE jogo_id = {0}",
                jogo.Id
            )
            .SingleAsync();
        pedidos.Should().Be(1);

        int linhasOutbox = await db
            .Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM outbox_message WHERE body LIKE {0}",
                $"%{jogo.Id}%"
            )
            .SingleAsync();
        linhasOutbox.Should().Be(1);
    }
}
