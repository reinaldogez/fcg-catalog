using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Um único Postgres compartilhado por toda a coleção; testes rodam em série.
[CollectionDefinition(Name)]
public class IntegrationCollection : ICollectionFixture<CatalogApiFactory>
{
    public const string Name = "Integration";
}
