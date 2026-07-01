namespace Fcg.Catalog.Api.Authorization;

// Fonte única dos nomes de policy e do valor da role de admin emitida pelo identity.
public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string SelfOrAdmin = "SelfOrAdmin";

    public const string RoleAdmin = "Administrador";
}
