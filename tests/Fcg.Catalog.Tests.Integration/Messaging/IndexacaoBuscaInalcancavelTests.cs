using Fcg.Catalog.Application.Events;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Messaging;

// Busca ligada com o cluster fora do ar: o consumer de indexação repete e, esgotadas as
// tentativas, a mensagem vai para a fila de erro. Corre num host derivado da fixture compartilhada
// e não da fixture da busca: lá o consumer da aplicação está sempre de pé na mesma fila, e um
// segundo host competiria pela mensagem com ele.
public class IndexacaoBuscaInalcancavelTests(CatalogApiFactory factory)
    : IntegrationTestBase(factory)
{
    private const string FilaDeErroDaIndexacao = "jogo-alterado.fcg-catalog_error";

    // Endereço sem ninguém escutando: recusa de conexão imediata.
    private const string EnderecoInalcancavel = "http://127.0.0.1:1";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ComBuscaInalcancavelAMensagemVaiParaAFilaDeErroEOJogoPermanece()
    {
        Guid jogoId = await SemearJogoAsync();
        await PurgarFilaDeErroAsync();

        using WebApplicationFactory<Program> hostSemBusca = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting(OpenSearchSettings.ChaveEndpoint, EnderecoInalcancavel)
        );

        IBusControl bus = hostSemBusca.Services.GetRequiredService<IBusControl>();
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StartAsync(cts.Token);
        try
        {
            await bus.Publish(new JogoAlteradoEvent { JogoId = jogoId }, cts.Token);

            (await AguardarAsync(async () => await MensagensNaFilaDeErroAsync() == 1))
                .Should()
                .BeTrue("esgotadas as tentativas, a mensagem vai para a fila de erro do broker");
        }
        finally
        {
            await PararAsync(bus);
            await PurgarFilaDeErroAsync();
        }

        await using AsyncServiceScope escopo = Factory.Services.CreateAsyncScope();
        (
            await escopo
                .ServiceProvider.GetRequiredService<CatalogDbContext>()
                .Jogos.AnyAsync(j => j.Id == jogoId)
        )
            .Should()
            .BeTrue("a falha do índice não toca a fonte da verdade");
    }

    private async Task<Guid> SemearJogoAsync()
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hollow Knight"), Preco.Criar(19.90m));

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Jogos.Add(jogo);
        await db.SaveChangesAsync();

        return jogo.Id;
    }

    private async Task<uint> MensagensNaFilaDeErroAsync()
    {
        ConnectionFactory fabrica = new() { Uri = new Uri(Factory.RabbitMqConnectionString) };
        await using IConnection conexao = await fabrica.CreateConnectionAsync();
        await using IChannel canal = await conexao.CreateChannelAsync();

        try
        {
            QueueDeclareOk fila = await canal.QueueDeclarePassiveAsync(FilaDeErroDaIndexacao);
            return fila.MessageCount;
        }
        catch (OperationInterruptedException)
        {
            // A fila de erro só passa a existir quando a primeira mensagem terminal chega nela.
            return 0;
        }
    }

    private async Task PurgarFilaDeErroAsync()
    {
        ConnectionFactory fabrica = new() { Uri = new Uri(Factory.RabbitMqConnectionString) };
        await using IConnection conexao = await fabrica.CreateConnectionAsync();
        await using IChannel canal = await conexao.CreateChannelAsync();

        try
        {
            await canal.QueuePurgeAsync(FilaDeErroDaIndexacao);
        }
        catch (OperationInterruptedException) { }
    }

    // O desligamento tem prazo próprio: parar com um token já cancelado deixa o consumidor
    // registrado no broker, e as mensagens dos testes seguintes iriam para um bus parado.
    private static async Task PararAsync(IBusControl bus)
    {
        using CancellationTokenSource cts = new(s_timeout);
        await bus.StopAsync(cts.Token);
    }

    private static async Task<bool> AguardarAsync(Func<Task<bool>> condicao)
    {
        DateTime limite = DateTime.UtcNow + s_timeout;
        while (DateTime.UtcNow < limite)
        {
            if (await condicao())
                return true;
            await Task.Delay(200);
        }
        return false;
    }
}
