using Microsoft.AspNetCore.Authorization;

namespace Fcg.Catalog.Api.Authorization;

// Owner na rota: o dono é o {usuarioId} da própria URL, conhecido sem tocar o banco.
public sealed class SelfOrAdminRequirement : IAuthorizationRequirement;
