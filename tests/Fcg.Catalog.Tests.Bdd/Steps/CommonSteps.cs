using Fcg.Catalog.Tests.Bdd.Support;
using FluentAssertions;
using Reqnroll;

namespace Fcg.Catalog.Tests.Bdd.Steps;

[Binding]
public class CommonSteps(CenarioEstado estado)
{
    [Then(@"recebo o status (\d+)")]
    public void EntaoReceboOStatus(int statusEsperado)
    {
        estado.UltimaResposta.Should().NotBeNull();
        ((int)estado.UltimaResposta!.StatusCode).Should().Be(statusEsperado);
    }
}
