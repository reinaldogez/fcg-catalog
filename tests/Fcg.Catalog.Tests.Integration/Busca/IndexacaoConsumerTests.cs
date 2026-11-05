using System.Net;
using System.Net.Http.Json;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Application.Events;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSearch.Client;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Busca;

// O consumer de indexação com o bus da aplicação de pé. Os eventos publicados aqui à mão somam-se
// aos que o outbox já entregou pelas escritas da API: o que se prova é que, seja qual for a ordem
// e a repetição, o índice termina no estado atual da fonte da verdade.
[Collection(BuscaCollection.Name)]
public class IndexacaoConsumerTests(CatalogApiComBuscaFactory factory)
{
    private static readonly TimeSpan s_teto = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task EventosForaDeOrdemDevemConvergirParaOEstadoAtual()
    {
        HttpClient admin = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        Guid id = await CriarAsync(admin, "Ordem original");

        (
            await admin.PutAsJsonAsync(
                $"/api/jogos/{id}",
                new { titulo = "Ordem intermediária", preco = 39.90m }
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (
            await admin.PutAsJsonAsync(
                $"/api/jogos/{id}",
                new
                {
                    titulo = "Ordem final",
                    preco = 19.90m,
                    descricao = "Estado que tem de vencer",
                }
            )
        ).StatusCode.Should().Be(HttpStatusCode.OK);

        // Dois eventos a mais para o mesmo jogo, entregues depois das três escritas: cada um faz
        // o consumer reler a fonte, e a escrita versionada descarta qualquer estado mais velho.
        IBus bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(new JogoAlteradoEvent { JogoId = id });
        await bus.Publish(new JogoAlteradoEvent { JogoId = id });

        (DocumentoJogo esperado, long versaoEsperada) = await EstadoAtualAsync(id);
        esperado.Titulo.Should().Be("Ordem final");

        (
            await AguardarAsync(async () =>
                await DocumentoIndexadoAsync(id) == (esperado, versaoEsperada)
            )
        )
            .Should()
            .BeTrue("o índice converge para o estado atual, com a versão relida do banco");
    }

    [Fact]
    public async Task OMesmoEventoDuasVezesDeveProduzirOMesmoDocumento()
    {
        HttpClient admin = factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        Guid id = await CriarAsync(admin, "Evento repetido");
        (DocumentoJogo esperado, long versao) = await EstadoAtualAsync(id);

        (await AguardarAsync(async () => await DocumentoIndexadoAsync(id) == (esperado, versao)))
            .Should()
            .BeTrue("a criação chega ao índice pelo outbox");

        IBus bus = factory.Services.GetRequiredService<IBus>();
        Guid[] entregas = [Guid.NewGuid(), Guid.NewGuid()];
        ObservadorDeConsumo observador = new(entregas);
        ConnectHandle conexao = bus.ConnectConsumeMessageObserver(observador);
        try
        {
            foreach (Guid messageId in entregas)
            {
                await bus.Publish(
                    new JogoAlteradoEvent { JogoId = id },
                    ctx => ctx.MessageId = messageId
                );
            }

            (await AguardarAsync(() => Task.FromResult(observador.Concluidos == 2)))
                .Should()
                .BeTrue("as duas entregas devem ser consumidas até o fim");
        }
        finally
        {
            conexao.Disconnect();
        }

        observador.Falhas.Should().Be(0, "reescrever o mesmo estado não é conflito");
        (await DocumentoIndexadoAsync(id)).Should().Be((esperado, versao));
    }

    private static async Task<Guid> CriarAsync(HttpClient admin, string titulo)
    {
        HttpResponseMessage criacao = await admin.PostAsJsonAsync(
            "/api/jogos",
            new { titulo, preco = 59.90m }
        );
        criacao.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!.Id;
    }

    // O documento e a versão que a fonte da verdade manda ter, pela mesma função da aplicação.
    private async Task<(DocumentoJogo Documento, long Versao)> EstadoAtualAsync(Guid id)
    {
        await using AsyncServiceScope escopo = factory.Services.CreateAsyncScope();
        Jogo jogo = await escopo
            .ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Jogos.AsNoTracking()
            .SingleAsync(j => j.Id == id);

        return (DocumentoJogo.De(jogo), VersaoDoJogo.De(jogo));
    }

    private async Task<(DocumentoJogo, long)?> DocumentoIndexadoAsync(Guid id)
    {
        GetResponse<DocumentoJogo> resposta = await factory.Inspecao.GetAsync<DocumentoJogo>(
            new DocumentPath<DocumentoJogo>(id),
            g => g.Index(MapeamentoDoIndice.NomeDoIndice)
        );

        return resposta.Found ? (resposta.Source, resposta.Version) : null;
    }

    private static async Task<bool> AguardarAsync(Func<Task<bool>> condicao)
    {
        DateTime limite = DateTime.UtcNow + s_teto;
        while (DateTime.UtcNow < limite)
        {
            if (await condicao())
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    // Fim do consumo sem exceção é o ACK. Conta só as entregas publicadas pelo teste, pelo
    // MessageId: o bus é o da aplicação e consome também o evento que o outbox entregou na criação.
    private sealed class ObservadorDeConsumo(IReadOnlyCollection<Guid> entregas)
        : IConsumeMessageObserver<JogoAlteradoEvent>
    {
        private int _concluidos;
        private int _falhas;

        public int Concluidos => Volatile.Read(ref _concluidos);

        public int Falhas => Volatile.Read(ref _falhas);

        public Task PreConsume(ConsumeContext<JogoAlteradoEvent> context) => Task.CompletedTask;

        public Task PostConsume(ConsumeContext<JogoAlteradoEvent> context)
        {
            if (context.MessageId is Guid messageId && entregas.Contains(messageId))
                Interlocked.Increment(ref _concluidos);
            return Task.CompletedTask;
        }

        public Task ConsumeFault(ConsumeContext<JogoAlteradoEvent> context, Exception exception)
        {
            if (context.MessageId is Guid messageId && entregas.Contains(messageId))
                Interlocked.Increment(ref _falhas);
            return Task.CompletedTask;
        }
    }
}
