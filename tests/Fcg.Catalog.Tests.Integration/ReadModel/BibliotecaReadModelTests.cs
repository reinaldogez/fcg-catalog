using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Infrastructure.DynamoDb;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.ReadModel;

public class BibliotecaReadModelTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    // Três itens deste tamanho passam de 1 MB somados e ficam abaixo do teto de 400 KB por item,
    // que é o que força a consulta a atravessar mais de uma página.
    private const int TamanhoDoNomeGrande = 390 * 1024;

    private IProjecaoBiblioteca Projecao =>
        Factory.Services.GetRequiredService<IProjecaoBiblioteca>();

    private IBibliotecaReadModel Consulta =>
        Factory.Services.GetRequiredService<IBibliotecaReadModel>();

    private IAmazonDynamoDB Cliente => Factory.Services.GetRequiredService<IAmazonDynamoDB>();

    [Fact]
    public async Task DeveDevolverOsSeisAtributosDaCompraAposProjetar()
    {
        ItemBibliotecaProjetado item = ItemDe(Guid.NewGuid(), Guid.NewGuid());

        await Projecao.ProjetarAsync(item);

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            item.UsuarioId
        );

        itens.Should().ContainSingle().Which.Should().Be(item);
    }

    [Fact]
    public async Task ItemPersistidoNaoDeveCarregarPagamentoIdentificadorTecnicoNemOrigem()
    {
        ItemBibliotecaProjetado item = ItemDe(Guid.NewGuid(), Guid.NewGuid());

        await Projecao.ProjetarAsync(item);

        Dictionary<string, AttributeValue> atributos = await LerBrutoAsync(
            item.UsuarioId,
            item.JogoId
        );

        atributos
            .Keys.Should()
            .BeEquivalentTo([
                DynamoDbTableBootstrap.ChaveParticao,
                DynamoDbTableBootstrap.ChaveOrdenacao,
                "usuarioId",
                "jogoId",
                "pedidoId",
                "nomeJogo",
                "preco",
                "adquiridoEm",
            ]);
    }

    [Fact]
    public async Task PrecoDeveVoltarExatoAposIdaEVolta()
    {
        ItemBibliotecaProjetado item = ItemDe(Guid.NewGuid(), Guid.NewGuid()) with
        {
            Preco = 19.90m,
        };

        await Projecao.ProjetarAsync(item);

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            item.UsuarioId
        );

        itens.Should().ContainSingle().Which.Preco.Should().Be(19.90m);

        Dictionary<string, AttributeValue> atributos = await LerBrutoAsync(
            item.UsuarioId,
            item.JogoId
        );

        // Atributo numérico, não texto: o tipo do atributo é parte do contrato do item.
        atributos["preco"].N.Should().NotBeNullOrEmpty();
        atributos["preco"].S.Should().BeNull();
    }

    [Fact]
    public async Task DataDeAquisicaoDeveSerPersistidaComoTextoIso8601ComSufixoZ()
    {
        DateTime adquiridoEm = new(2026, 7, 31, 18, 4, 5, DateTimeKind.Utc);
        ItemBibliotecaProjetado item = ItemDe(Guid.NewGuid(), Guid.NewGuid()) with
        {
            AdquiridoEm = adquiridoEm,
        };

        await Projecao.ProjetarAsync(item);

        Dictionary<string, AttributeValue> atributos = await LerBrutoAsync(
            item.UsuarioId,
            item.JogoId
        );

        atributos["adquiridoEm"].S.Should().StartWith("2026-07-31T18:04:05").And.EndWith("Z");
        atributos["adquiridoEm"].N.Should().BeNull();

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            item.UsuarioId
        );

        itens.Should().ContainSingle().Which.AdquiridoEm.Should().Be(adquiridoEm);
    }

    [Fact]
    public async Task ProjetarOMesmoEventoDuasVezesDeveDeixarUmItemIdentico()
    {
        ItemBibliotecaProjetado item = ItemDe(Guid.NewGuid(), Guid.NewGuid());

        await Projecao.ProjetarAsync(item);
        Dictionary<string, AttributeValue> primeira = await LerBrutoAsync(
            item.UsuarioId,
            item.JogoId
        );

        await Projecao.ProjetarAsync(item);
        Dictionary<string, AttributeValue> segunda = await LerBrutoAsync(
            item.UsuarioId,
            item.JogoId
        );

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            item.UsuarioId
        );

        itens.Should().ContainSingle();

        // Byte a byte: nenhum atributo é gerado no instante do processamento, então a reescrita
        // não pode ter mexido em nada.
        segunda
            .Should()
            .BeEquivalentTo(primeira, opcoes => opcoes.ComparingByMembers<AttributeValue>());
    }

    [Fact]
    public async Task ConsultaDeveAtravessarAContinuacaoDePaginaEDevolverTodosOsItens()
    {
        var usuarioId = Guid.NewGuid();
        string nomeGrande = new('j', TamanhoDoNomeGrande);

        List<Guid> jogos = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        foreach (Guid jogoId in jogos)
        {
            await Projecao.ProjetarAsync(ItemDe(usuarioId, jogoId) with { NomeJogo = nomeGrande });
        }

        // A instância local devolve os três de uma vez apesar de somarem mais de 1 MB: ela não
        // aplica o teto de página do serviço. Um item por página é o que reproduz a continuação
        // aqui — sem isso o teste passa mesmo com a consulta parando na primeira página.
        DynamoDbBibliotecaStore consulta = new(
            Cliente,
            Factory.Services.GetRequiredService<DynamoDbSettings>(),
            limiteDeItensPorPagina: 1
        );

        IReadOnlyList<ItemBibliotecaProjetado> itens = await consulta.ListarPorUsuarioAsync(
            usuarioId
        );

        itens.Select(i => i.JogoId).Should().BeEquivalentTo(jogos);
    }

    [Fact]
    public async Task DoisJogosDoMesmoUsuarioDevemVirarDoisItensNaMesmaParticao()
    {
        var usuarioId = Guid.NewGuid();
        ItemBibliotecaProjetado primeiro = ItemDe(usuarioId, Guid.NewGuid());
        ItemBibliotecaProjetado segundo = ItemDe(usuarioId, Guid.NewGuid());

        await Projecao.ProjetarAsync(primeiro);
        await Projecao.ProjetarAsync(segundo);

        IReadOnlyList<ItemBibliotecaProjetado> itens = await Consulta.ListarPorUsuarioAsync(
            usuarioId
        );

        itens.Should().HaveCount(2).And.BeEquivalentTo([primeiro, segundo]);
    }

    private static ItemBibliotecaProjetado ItemDe(Guid usuarioId, Guid jogoId) =>
        new(
            usuarioId,
            jogoId,
            Guid.NewGuid(),
            "Jogo de Teste",
            49.99m,
            new DateTime(2026, 7, 31, 18, 4, 5, DateTimeKind.Utc)
        );

    private async Task<Dictionary<string, AttributeValue>> LerBrutoAsync(
        Guid usuarioId,
        Guid jogoId
    )
    {
        GetItemResponse resposta = await Cliente.GetItemAsync(
            new GetItemRequest
            {
                TableName = CatalogApiFactory.NomeDaTabelaDeLeitura,
                Key = new Dictionary<string, AttributeValue>
                {
                    [DynamoDbTableBootstrap.ChaveParticao] = new($"USER#{usuarioId}"),
                    [DynamoDbTableBootstrap.ChaveOrdenacao] = new($"JOGO#{jogoId}"),
                },
                ConsistentRead = true,
            }
        );

        resposta.Item.Should().NotBeNull();

        return resposta.Item!;
    }
}
