using Fcg.Catalog.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Fcg.Catalog.Api.Authorization;

// Resource-based: a action carrega o Pedido e passa como recurso. A regra de propriedade vive no
// agregado (pedido.PertenceAo — Tell-Don't-Ask); aqui só decidimos admin-ou-dono.
public sealed class PedidoOwnershipHandler
    : AuthorizationHandler<PedidoOwnershipRequirement, Pedido>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PedidoOwnershipRequirement requirement,
        Pedido pedido
    )
    {
        bool isAdmin = context.User.IsInRole(AuthorizationPolicies.RoleAdmin);

        bool isDono =
            context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is string sub
            && Guid.TryParse(sub, out Guid usuarioId)
            && pedido.PertenceAo(usuarioId);

        if (isAdmin || isDono)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
