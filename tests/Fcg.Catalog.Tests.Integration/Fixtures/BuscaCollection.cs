using Xunit;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Um único host com a busca ligada para todas as classes da busca: cada uma pagaria, senão, quatro
// containers e uma JVM. Os testes da coleção rodam em série.
[CollectionDefinition(Name)]
public class BuscaCollection : ICollectionFixture<CatalogApiComBuscaFactory>
{
    public const string Name = "Busca";
}
