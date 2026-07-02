using Fcg.Catalog.Application;
using Fcg.Catalog.Application.UseCases.Biblioteca;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Application.UseCases.Pedidos;
using Fcg.Catalog.Infrastructure;
using Fcg.Catalog.Infrastructure.Seed;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Composition;

// Rede de segurança da composição-raiz: valida o grafo de DI de PRODUÇÃO (AddApplication +
// AddInfrastructure) sem os supridores de teste da fixture. Se qualquer serviço consumido por um
// use case — ou resolvido via GetRequiredService no boot do Job — não tiver registro de produção,
// a resolução aqui lança e o teste falha, pegando o gap antes do boot no cluster.
//
// Standalone (sem containers): a construção do DbContext (UseNpgsql) e do bus é lazy — não conecta.
// A classe fica fora da IntegrationCollection para não acionar o fixture que sobe Postgres/RabbitMQ.
public class ComposicaoRaizTests
{
    [Fact]
    public async Task GrafoDeProducaoResolveTodosOsUseCasesSemSupridoresDeTeste()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // Strings quaisquer: só a construção do grafo é exercitada, nada conecta.
                    ["ConnectionStrings:Catalog"] =
                        "Host=localhost;Database=catalog;Username=u;Password=p",
                    ["RabbitMq:Host"] = "localhost",
                    ["RabbitMq:Username"] = "guest",
                    ["RabbitMq:Password"] = "guest",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // Descarte assíncrono: o MassTransit registra serviços só-IAsyncDisposable (UsageTracker),
        // e o Dispose síncrono do provider lançaria.
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        // Nenhum destes pode lançar — todos precisam de registro de produção. O CriarPedidoUseCase
        // é o que expunha o gap (depende de IPedidoDomainService); o CatalogSeeder é o alvo do Job.
        Action resolver = () =>
        {
            sp.GetRequiredService<CriarPedidoUseCase>();
            sp.GetRequiredService<ObterPedidoPorIdUseCase>();
            sp.GetRequiredService<AprovarPedidoUseCase>();
            sp.GetRequiredService<RejeitarPedidoUseCase>();

            sp.GetRequiredService<CriarJogoUseCase>();
            sp.GetRequiredService<ObterJogoPorIdUseCase>();
            sp.GetRequiredService<ListarJogosUseCase>();
            sp.GetRequiredService<AtualizarJogoUseCase>();
            sp.GetRequiredService<DesativarJogoUseCase>();

            sp.GetRequiredService<ObterBibliotecaDoUsuarioUseCase>();

            sp.GetRequiredService<CatalogSeeder>();
        };

        resolver.Should().NotThrow();
    }
}
