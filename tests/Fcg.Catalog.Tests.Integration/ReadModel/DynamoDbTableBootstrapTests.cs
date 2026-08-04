using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Fcg.Catalog.Infrastructure.DynamoDb;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Catalog.Tests.Integration.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.ReadModel;

// Cada teste usa um nome de tabela próprio para não interferir na tabela que a fixture já criou.
public class DynamoDbTableBootstrapTests(CatalogApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task TabelaAusenteDeveSerCriadaComChavesStringOnDemandESemTtl()
    {
        IAmazonDynamoDB cliente = Factory.Services.GetRequiredService<IAmazonDynamoDB>();
        string tabela = NomeDeTabelaInedito();

        await CriarBootstrap(tabela, Factory.DynamoDbServiceUrl).GarantirTabelaAsync();

        DescribeTableResponse descricao = await cliente.DescribeTableAsync(tabela);

        descricao
            .Table.KeySchema.Should()
            .SatisfyRespectively(
                chave =>
                {
                    chave.AttributeName.Should().Be(DynamoDbTableBootstrap.ChaveParticao);
                    chave.KeyType.Should().Be(KeyType.HASH);
                },
                chave =>
                {
                    chave.AttributeName.Should().Be(DynamoDbTableBootstrap.ChaveOrdenacao);
                    chave.KeyType.Should().Be(KeyType.RANGE);
                }
            );

        descricao
            .Table.AttributeDefinitions.Should()
            .OnlyContain(atributo => atributo.AttributeType == ScalarAttributeType.S);

        descricao.Table.BillingModeSummary.BillingMode.Should().Be(BillingMode.PAY_PER_REQUEST);

        DescribeTimeToLiveResponse ttl = await cliente.DescribeTimeToLiveAsync(tabela);

        ttl.TimeToLiveDescription.TimeToLiveStatus.Should().Be(TimeToLiveStatus.DISABLED);

        await cliente.DeleteTableAsync(tabela);
    }

    [Fact]
    public async Task SemServiceUrlNenhumaTabelaDeveSerCriada()
    {
        IAmazonDynamoDB cliente = Factory.Services.GetRequiredService<IAmazonDynamoDB>();
        string tabela = NomeDeTabelaInedito();

        // O cliente aponta a instância local e conseguiria criar; o que segura é a ausência da
        // chave de endpoint, que é o contrato do ambiente de nuvem.
        await CriarBootstrap(tabela, serviceUrl: null).GarantirTabelaAsync();

        Func<Task> descrever = () => cliente.DescribeTableAsync(tabela);

        await descrever.Should().ThrowAsync<ResourceNotFoundException>();
    }

    private static string NomeDeTabelaInedito() => $"biblioteca-{Guid.NewGuid():N}";

    private DynamoDbTableBootstrap CriarBootstrap(string tabela, string? serviceUrl)
    {
        Dictionary<string, string?> entradas = new()
        {
            [DynamoDbSettings.ChaveTableName] = tabela,
            [DynamoDbSettings.ChaveServiceUrl] = serviceUrl,
        };

        var settings = DynamoDbSettings.Ler(
            new ConfigurationBuilder().AddInMemoryCollection(entradas).Build()
        );

        return new DynamoDbTableBootstrap(
            Factory.Services.GetRequiredService<IAmazonDynamoDB>(),
            settings,
            NullLogger<DynamoDbTableBootstrap>.Instance
        );
    }
}
