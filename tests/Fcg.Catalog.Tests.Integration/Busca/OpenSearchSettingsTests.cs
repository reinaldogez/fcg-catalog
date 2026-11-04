using Fcg.Catalog.Infrastructure.Busca;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Busca;

// Standalone (sem containers): o config-gate e o fail-fast das chaves de busca são decididos na
// leitura, antes de qualquer cliente existir.
public class OpenSearchSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EndpointAusenteOuEmBrancoDeveDesligarABusca(string? endpoint)
    {
        OpenSearchSettings settings = Ler(endpoint, useSigV4: null);

        settings.Habilitado.Should().BeFalse();
        settings.Endpoint.Should().BeNull();
        settings.UseSigV4.Should().BeFalse();
    }

    // Sem endpoint a assinatura não é lida: um ambiente sem busca não precisa declará-la direito.
    [Fact]
    public void AssinaturaIlegivelSemEndpointNaoDeveSerLida()
    {
        Action ler = () => Ler(endpoint: null, useSigV4: "talvez");

        ler.Should().NotThrow();
    }

    [Fact]
    public void EndpointHttpSemAssinaturaDeveLigarABusca()
    {
        OpenSearchSettings settings = Ler(" http://localhost:9200 ", useSigV4: null);

        settings.Habilitado.Should().BeTrue();
        settings.Endpoint.Should().Be(new Uri("http://localhost:9200"));
        settings.UseSigV4.Should().BeFalse();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void EndpointHttpsComAssinaturaDeveLigarABuscaAssinada(string useSigV4)
    {
        OpenSearchSettings settings = Ler("https://busca.exemplo.local", useSigV4);

        settings.Habilitado.Should().BeTrue();
        settings.UseSigV4.Should().BeTrue();
    }

    [Theory]
    [InlineData("busca.exemplo.local")]
    [InlineData("/relativo")]
    [InlineData("ftp://busca.exemplo.local")]
    [InlineData("não é url")]
    public void EndpointQueNaoEUrlHttpAbsolutaDeveSerRecusado(string endpoint)
    {
        Action ler = () => Ler(endpoint, useSigV4: null);

        ler.Should().Throw<InvalidOperationException>().WithMessage("*OpenSearch:Endpoint*");
    }

    [Fact]
    public void AssinaturaComEndpointHttpDeveSerRecusada()
    {
        Action ler = () => Ler("http://busca.exemplo.local", useSigV4: "true");

        ler.Should().Throw<InvalidOperationException>().WithMessage("*OpenSearch:UseSigV4*");
    }

    [Fact]
    public void AssinaturaIlegivelComEndpointDeveSerRecusada()
    {
        Action ler = () => Ler("https://busca.exemplo.local", useSigV4: "sim");

        ler.Should().Throw<InvalidOperationException>().WithMessage("*OpenSearch:UseSigV4*");
    }

    private static OpenSearchSettings Ler(string? endpoint, string? useSigV4)
    {
        Dictionary<string, string?> entradas = new()
        {
            [OpenSearchSettings.ChaveEndpoint] = endpoint,
            [OpenSearchSettings.ChaveUseSigV4] = useSigV4,
        };

        return OpenSearchSettings.Ler(
            new ConfigurationBuilder().AddInMemoryCollection(entradas).Build()
        );
    }
}
