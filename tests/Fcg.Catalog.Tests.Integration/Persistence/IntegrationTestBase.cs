using Fcg.Catalog.Tests.Integration.Fixtures;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Persistence;

[Collection(IntegrationCollection.Name)]
public abstract class IntegrationTestBase(CatalogApiFactory factory) : IAsyncLifetime
{
    protected CatalogApiFactory Factory { get; } = factory;

    public Task InitializeAsync() => Factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
