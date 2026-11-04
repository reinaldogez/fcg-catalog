using Fcg.Catalog.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenSearch.Client;
using OpenSearch.Net;
using OpenSearch.Net.Auth.AwsSigV4;

namespace Fcg.Catalog.Infrastructure.Busca;

public static class BuscaExtensions
{
    // Um terço do teto de integração do gateway: a busca indisponível responde com o erro do
    // próprio serviço antes de a borda cortar a requisição com um erro genérico.
    private static readonly TimeSpan s_timeoutDaRequisicao = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddCatalogSearch(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var settings = OpenSearchSettings.Ler(configuration);

        services.AddSingleton(settings);

        if (!settings.Habilitado)
        {
            services.AddSingleton<IndiceDeJogosDesligado>();
            services.AddSingleton<IIndiceDeJogos>(sp =>
                sp.GetRequiredService<IndiceDeJogosDesligado>()
            );
            services.AddSingleton<IPreparacaoDoIndice>(sp =>
                sp.GetRequiredService<IndiceDeJogosDesligado>()
            );
            services.AddSingleton<IBuscaDeJogos>(sp =>
                sp.GetRequiredService<IndiceDeJogosDesligado>()
            );

            return services;
        }

        // Construído aqui, e não na primeira resolução: com assinatura ligada, credencial e região
        // são resolvidas pela cadeia padrão do SDK no construtor da conexão, e a ausência delas
        // tem de derrubar o boot em vez de virar recusa na primeira busca.
        IOpenSearchClient cliente = CriarCliente(settings);

        services.AddSingleton(cliente);

        // As portas resolvem a mesma instância: a classe não guarda estado além do cliente, que
        // já é singleton.
        services.AddSingleton<OpenSearchIndiceDeJogos>();
        services.AddSingleton<IIndiceDeJogos>(sp =>
            sp.GetRequiredService<OpenSearchIndiceDeJogos>()
        );
        services.AddSingleton<IPreparacaoDoIndice>(sp =>
            sp.GetRequiredService<OpenSearchIndiceDeJogos>()
        );
        services.AddSingleton<IBuscaDeJogos>(sp =>
            sp.GetRequiredService<OpenSearchIndiceDeJogos>()
        );

        // Fora da prontidão de propósito: o domínio de busca pode nascer depois do serviço, e
        // tirar do balanceamento por causa dele levaria junto catálogo, pedidos e biblioteca.
        // A falha fica restrita ao endpoint de busca; aqui o estado só é informado.
        services.AddHealthChecks().AddCheck<OpenSearchHealthCheck>("opensearch");

        // Só com a busca ligada: desligada, não há cluster a esperar nem índice a preencher.
        services.AddHostedService<ReindexacaoAutomatica>();

        return services;
    }

    private static IOpenSearchClient CriarCliente(OpenSearchSettings settings)
    {
        Uri endpoint = settings.Endpoint!;

        // Nó único: o domínio é endereçado por um nome só, e sondar ou rodiziar nós não teria
        // alvo. Sem retry no cliente: na escrita quem repete é o consumer, e na leitura falhar
        // rápido é o contrato.
        SingleNodeConnectionPool pool = new(endpoint);

        ConnectionSettings configuracao = (
            settings.UseSigV4
                ? new ConnectionSettings(pool, new AwsSigV4HttpConnection())
                : new ConnectionSettings(pool)
        )
            .RequestTimeout(s_timeoutDaRequisicao)
            .MaximumRetries(0);

        return new OpenSearchClient(configuracao);
    }
}
