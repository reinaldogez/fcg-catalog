using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

public class BibliotecaEndpointsTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    // Endereço sem ninguém escutando: recusa de conexão imediata, sem derrubar o container que os
    // outros testes da coleção compartilham.
    private const string EnderecoInalcancavel = "http://127.0.0.1:1";

    private static readonly DateTime s_instante = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private IProjecaoBiblioteca Projecao =>
        Factory.Services.GetRequiredService<IProjecaoBiblioteca>();

    [Fact]
    public async Task ObterBibliotecaVaziaDeveRetornar200ComListaVazia()
    {
        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());

        HttpResponseMessage resposta = await client.GetAsync($"/api/biblioteca/{Guid.NewGuid()}");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        IReadOnlyList<ItemBibliotecaResponse>? itens = await resposta.Content.ReadFromJsonAsync<
            IReadOnlyList<ItemBibliotecaResponse>
        >();
        itens.Should().NotBeNull();
        itens!.Should().BeEmpty();
    }

    [Fact]
    public async Task ItemProjetadoDeveVirar200ComOsSeisCamposESemIdentificadorTecnico()
    {
        var usuarioId = Guid.NewGuid();
        ItemBibliotecaProjetado projetado = ItemDe(usuarioId, Guid.NewGuid(), s_instante);
        await Projecao.ProjetarAsync(projetado);

        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        HttpResponseMessage resposta = await client.GetAsync($"/api/biblioteca/{usuarioId}");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sobre o JSON cru, não sobre o DTO: desserializar para o tipo do contrato descartaria em
        // silêncio qualquer campo a mais, e o que se prova aqui é a forma do corpo.
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        JsonElement item = documento.RootElement.EnumerateArray().Single();

        item.EnumerateObject()
            .Select(campo => campo.Name)
            .Should()
            .BeEquivalentTo([
                "usuarioId",
                "jogoId",
                "pedidoId",
                "nomeJogo",
                "preco",
                "adquiridoEm",
            ]);

        IReadOnlyList<ItemBibliotecaResponse>? itens = await resposta.Content.ReadFromJsonAsync<
            IReadOnlyList<ItemBibliotecaResponse>
        >();
        itens!
            .Single()
            .Should()
            .Be(
                new ItemBibliotecaResponse(
                    usuarioId,
                    projetado.JogoId,
                    projetado.PedidoId,
                    projetado.NomeJogo,
                    projetado.Preco,
                    projetado.AdquiridoEm
                )
            );
    }

    [Fact]
    public async Task DoisItensDevemVirComAAquisicaoMaisRecentePrimeiro()
    {
        var usuarioId = Guid.NewGuid();

        // A chave de ordenação do armazenamento é o identificador do jogo. Dar o menor ao item mais
        // antigo faz a consulta devolver exatamente o inverso da ordem esperada — com jogos
        // sorteados, metade das execuções passaria mesmo sem o caso de uso ordenar.
        (Guid menor, Guid maior) = DoisJogosEmOrdemDeChave();
        ItemBibliotecaProjetado antigo = ItemDe(usuarioId, menor, s_instante);
        ItemBibliotecaProjetado recente = ItemDe(usuarioId, maior, s_instante.AddDays(1));

        await Projecao.ProjetarAsync(antigo);
        await Projecao.ProjetarAsync(recente);

        HttpClient client = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        IReadOnlyList<ItemBibliotecaResponse>? itens = await client.GetFromJsonAsync<
            IReadOnlyList<ItemBibliotecaResponse>
        >($"/api/biblioteca/{usuarioId}");

        itens!.Select(item => item.JogoId).Should().Equal(recente.JogoId, antigo.JogoId);
    }

    [Fact]
    public async Task ComReadModelInalcancavelDeveResponder5xxEmVezDeListaVazia()
    {
        // Host derivado com o modelo de leitura apontado para um endereço morto. Sem desvio para o
        // banco relacional, a falha tem de chegar ao cliente: 200 com lista vazia diria ao usuário
        // que ele não comprou nada.
        using WebApplicationFactory<Program> hostSemReadModel = Factory.WithWebHostBuilder(
            builder =>
            {
                builder.UseSetting("DynamoDb:ServiceUrl", EnderecoInalcancavel);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAmazonDynamoDB>();
                    services.AddSingleton<IAmazonDynamoDB>(_ => ClienteInalcancavel());
                });
            }
        );

        HttpClient client = hostSemReadModel.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {JwtTestTokens.TokenAdmin()}");

        HttpResponseMessage resposta = await client.GetAsync($"/api/biblioteca/{Guid.NewGuid()}");

        ((int)resposta.StatusCode).Should().BeGreaterThanOrEqualTo(500);
    }

    [Fact]
    public async Task ComReadModelVazioEItemNoPostgresCriarPedidoDeveResponderConflito()
    {
        var usuarioId = Guid.NewGuid();
        HttpClient admin = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenAdmin());
        HttpResponseMessage criacao = await admin.PostAsJsonAsync(
            "/api/jogos",
            new { titulo = "Hollow Knight", preco = 19.90m }
        );
        JogoResponse jogo = (await criacao.Content.ReadFromJsonAsync<JogoResponse>())!;

        await CreditarNaFonteDaVerdadeAsync(usuarioId, jogo.Id);

        HttpClient dono = Factory.CreateAuthenticatedClient(JwtTestTokens.TokenUsuario(usuarioId));

        // Premissa do teste: a projeção não aconteceu, então a biblioteca servida está vazia.
        IReadOnlyList<ItemBibliotecaResponse>? itens = await dono.GetFromJsonAsync<
            IReadOnlyList<ItemBibliotecaResponse>
        >($"/api/biblioteca/{usuarioId}");
        itens!.Should().BeEmpty();

        HttpResponseMessage pedido = await dono.PostAsJsonAsync(
            "/api/pedidos",
            new { jogoId = jogo.Id }
        );

        // A invariante de compra não migrou para o modelo de leitura: ela continua perguntando à
        // fonte da verdade, e é isso que impede a janela de consistência de virar compra duplicada.
        pedido.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DescricaoNaDocumentacaoDeveDeclararConsistenciaEventualEApontarAConsultaDePedido()
    {
        // O documento OpenAPI só é servido em desenvolvimento; o host derivado é o que torna a
        // asserção sobre o documento real possível, em vez de conferir só o metadado que o alimenta.
        using WebApplicationFactory<Program> hostDeDesenvolvimento = Factory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Development")
        );

        using var documento = JsonDocument.Parse(
            await hostDeDesenvolvimento.CreateClient().GetStringAsync("/openapi/v1.json")
        );

        string descricao = documento
            .RootElement.GetProperty("paths")
            .GetProperty("/api/biblioteca/{usuarioId}")
            .GetProperty("get")
            .GetProperty("description")
            .GetString()!;

        descricao.Should().Contain("consistência eventual");
        descricao.Should().Contain("GET /api/pedidos/{id}");
    }

    // Aponta para o mesmo endereço morto do host, mas sem a cadeia de retry do SDK: com a política
    // padrão uma única tentativa segura mais de dez segundos. O que se exercita continua sendo o
    // modelo de leitura inalcançável.
    private static IAmazonDynamoDB ClienteInalcancavel() =>
        new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig
            {
                ServiceURL = EnderecoInalcancavel,
                AuthenticationRegion = "us-east-1",
                MaxErrorRetry = 0,
                Timeout = TimeSpan.FromSeconds(2),
            }
        );

    private static ItemBibliotecaProjetado ItemDe(
        Guid usuarioId,
        Guid jogoId,
        DateTime adquiridoEm
    ) => new(usuarioId, jogoId, Guid.NewGuid(), "Hollow Knight", 19.90m, adquiridoEm);

    // Ordinal sobre o texto do identificador: é assim que o armazenamento compara a chave de
    // ordenação, que é o prefixo do jogo concatenado com esse mesmo texto.
    private static (Guid Menor, Guid Maior) DoisJogosEmOrdemDeChave()
    {
        Guid[] jogos = [Guid.NewGuid(), Guid.NewGuid()];
        Array.Sort(jogos, (a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));
        return (jogos[0], jogos[1]);
    }

    // Credita direto no banco relacional, sem passar pelo evento: é o estado que separa a fonte da
    // verdade do modelo de leitura, que é justamente o que este teste precisa montar.
    private async Task CreditarNaFonteDaVerdadeAsync(Guid usuarioId, Guid jogoId)
    {
        await using AsyncServiceScope escopo = Factory.Services.CreateAsyncScope();
        IItemBibliotecaRepository repositorio =
            escopo.ServiceProvider.GetRequiredService<IItemBibliotecaRepository>();
        IUnitOfWork unitOfWork = escopo.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repositorio.AdicionarAsync(ItemBiblioteca.Criar(usuarioId, jogoId));
        await unitOfWork.SalvarAlteracoesAsync();
    }
}
