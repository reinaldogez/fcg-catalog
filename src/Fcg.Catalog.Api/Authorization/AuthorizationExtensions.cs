using Microsoft.AspNetCore.Authorization;

namespace Fcg.Catalog.Api.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddCatalogAuthorization(this IServiceCollection services)
    {
        services
            .AddAuthorizationBuilder()
            // Mutação de Jogo: policy declarativa por role.
            .AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(AuthorizationPolicies.RoleAdmin)
            )
            // Biblioteca: owner na rota ou admin, sem tocar o banco.
            .AddPolicy(
                AuthorizationPolicies.SelfOrAdmin,
                policy => policy.AddRequirements(new SelfOrAdminRequirement())
            );

        services.AddScoped<IAuthorizationHandler, SelfOrAdminHandler>();
        services.AddScoped<IAuthorizationHandler, PedidoOwnershipHandler>();

        return services;
    }
}
