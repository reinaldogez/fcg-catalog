using Microsoft.AspNetCore.Authorization;

namespace Fcg.Catalog.Api.Authorization;

// Ownership do pedido só é conhecido após carregar o agregado (não está na rota).
public sealed class PedidoOwnershipRequirement : IAuthorizationRequirement;
