using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Fcg.Catalog.Api.OpenApi;

// Descreve o esquema Bearer no documento OpenAPI (botão Authorize no Swagger UI). Só documenta —
// a validação real do token é do handler JwtBearer.
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        OpenApiSecurityScheme esquema = new()
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT RS256 emitido pelo fcg-identity.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = esquema;

        document.Security ??= [];
        document.Security.Add(
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            }
        );

        return Task.CompletedTask;
    }
}
