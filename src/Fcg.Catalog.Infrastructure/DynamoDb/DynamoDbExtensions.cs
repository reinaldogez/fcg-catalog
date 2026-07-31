using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Fcg.Catalog.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fcg.Catalog.Infrastructure.DynamoDb;

public static class DynamoDbExtensions
{
    // Região de assinatura usada apenas com endpoint local: o SDK exige uma para o SigV4, e a
    // instância local não a interpreta. Na nuvem a região vem da variável flat que o SDK já lê.
    private const string RegiaoDeAssinaturaLocal = "us-east-1";

    public static IServiceCollection AddCatalogReadModelStore(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var settings = DynamoDbSettings.Ler(configuration);

        services.AddSingleton(settings);

        services.AddSingleton<IAmazonDynamoDB>(_ => CriarCliente(settings));
        services.AddSingleton<DynamoDbTableBootstrap>();

        // As duas portas resolvem a mesma instância: a classe não guarda estado e suas duas
        // dependências já são singleton, então um objeto por escopo não compraria nada.
        services.AddSingleton(sp => new DynamoDbBibliotecaStore(
            sp.GetRequiredService<IAmazonDynamoDB>(),
            sp.GetRequiredService<DynamoDbSettings>()
        ));
        services.AddSingleton<IBibliotecaReadModel>(sp =>
            sp.GetRequiredService<DynamoDbBibliotecaStore>()
        );
        services.AddSingleton<IProjecaoBiblioteca>(sp =>
            sp.GetRequiredService<DynamoDbBibliotecaStore>()
        );

        return services;
    }

    private static IAmazonDynamoDB CriarCliente(DynamoDbSettings settings)
    {
        AmazonDynamoDBConfig config = new();

        if (!settings.UsaEndpointLocal)
        {
            return new AmazonDynamoDBClient(config);
        }

        config.ServiceURL = settings.ServiceUrl;
        config.AuthenticationRegion = RegiaoDeAssinaturaLocal;

        // A instância local aceita qualquer credencial mas recusa nenhuma, e a cadeia padrão do
        // SDK falharia numa máquina sem perfil da AWS configurado.
        return new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"), config);
    }
}
