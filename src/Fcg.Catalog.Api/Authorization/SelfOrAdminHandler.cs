using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Fcg.Catalog.Api.Authorization;

// Roda antes da action: compara a claim sub com o {usuarioId} da rota. Admin passa sempre.
public sealed class SelfOrAdminHandler : AuthorizationHandler<SelfOrAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SelfOrAdminRequirement requirement
    )
    {
        if (context.User.IsInRole(AuthorizationPolicies.RoleAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (
            context.Resource is HttpContext httpContext
            && httpContext.Request.RouteValues.TryGetValue("usuarioId", out object? valorRota)
            && Guid.TryParse(valorRota?.ToString(), out Guid usuarioIdRota)
            && context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is string sub
            && Guid.TryParse(sub, out Guid usuarioIdClaim)
            && usuarioIdRota == usuarioIdClaim
        )
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
