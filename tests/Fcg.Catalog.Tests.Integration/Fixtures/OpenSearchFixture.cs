using OpenSearch.Client;
using Testcontainers.OpenSearch;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Fixture própria do índice de busca: a fixture compartilhada da suíte permanece sem OpenSearch, e
// só as classes que exercitam o índice pagam o container.
public sealed class OpenSearchFixture : IAsyncLifetime
{
    // A mesma versão que o domínio gerenciado reporta. Sem o plugin de segurança: o container fala
    // http sem credencial, que é o modo sem assinatura do serviço.
    private readonly OpenSearchContainer _openSearch = new OpenSearchBuilder(
        "opensearchproject/opensearch:3.7.0"
    )
        .WithSecurityEnabled(false)
        // Heap fixo e pequeno: o default da imagem reserva metade da memória do host.
        .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
        .Build();

    private IOpenSearchClient? _inspecao;

    public string Endpoint { get; private set; } = string.Empty;

    // Cliente direto, só para inspecionar e preparar o cluster por fora das portas — mapping,
    // settings, documento bruto, refresh. Nenhum caminho de produção passa por aqui.
    public IOpenSearchClient Inspecao =>
        _inspecao ?? throw new InvalidOperationException("Fixture não inicializada.");

    public async Task InitializeAsync()
    {
        await _openSearch.StartAsync();

        Endpoint = _openSearch.GetConnectionString();
        _inspecao = new OpenSearchClient(new ConnectionSettings(new Uri(Endpoint)));
    }

    public Task DisposeAsync() => _openSearch.DisposeAsync().AsTask();
}
