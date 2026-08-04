using Microsoft.Extensions.Configuration;

namespace Fcg.Catalog.Infrastructure.DynamoDb;

// Leitura isolada das duas chaves do armazenamento do read model, para que a decisão de endpoint
// seja asserível sem construir cliente algum. O contrato é agnóstico de ambiente: ServiceUrl
// presente aponta uma instância local, ausente deixa o SDK resolver endpoint regional e
// credencial. A região não vira chave nossa — fica na variável flat que o SDK já lê sozinho.
public sealed class DynamoDbSettings
{
    public const string ChaveTableName = "DynamoDb:TableName";
    public const string ChaveServiceUrl = "DynamoDb:ServiceUrl";

    private DynamoDbSettings(string tableName, string? serviceUrl)
    {
        TableName = tableName;
        ServiceUrl = serviceUrl;
    }

    public string TableName { get; }

    public string? ServiceUrl { get; }

    // Endpoint local é o único gatilho do que é específico de desenvolvimento: cliente apontado
    // para fora da AWS e criação da tabela pela própria aplicação.
    public bool UsaEndpointLocal => !string.IsNullOrWhiteSpace(ServiceUrl);

    // Fail-fast na composição: sem o nome da tabela não há read model, e um boot que siga em
    // frente só descobre isso na primeira leitura de biblioteca, já servindo tráfego.
    public static DynamoDbSettings Ler(IConfiguration configuration)
    {
        string? tableName = configuration[ChaveTableName];

        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new InvalidOperationException($"{ChaveTableName} não configurado.");
        }

        string? serviceUrl = configuration[ChaveServiceUrl];

        return new DynamoDbSettings(
            tableName,
            string.IsNullOrWhiteSpace(serviceUrl) ? null : serviceUrl
        );
    }
}
