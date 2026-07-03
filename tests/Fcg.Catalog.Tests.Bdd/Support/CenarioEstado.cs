namespace Fcg.Catalog.Tests.Bdd.Support;

// Estado compartilhado entre os steps de um mesmo cenário — uma instância nova é registrada no
// IObjectContainer a cada cenário, então nada vaza de um cenário para o outro.
public class CenarioEstado
{
    public Guid UsuarioId { get; } = Guid.NewGuid();
    public Guid JogoId { get; set; }
    public Guid PedidoId { get; set; }
    public HttpResponseMessage? UltimaResposta { get; set; }
}
