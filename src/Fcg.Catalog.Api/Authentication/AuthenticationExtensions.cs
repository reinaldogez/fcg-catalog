using System.Security.Claims;
using Fcg.Catalog.Application.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fcg.Catalog.Api.Authentication;

public static class AuthenticationExtensions
{
    // Resource server: valida RS256 do identity descobrindo a chave pública no JWKS dele.
    public static IServiceCollection AddCatalogAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        // Fail-fast no startup: sem JwksUri/Issuer/Audience o resource server não valida nada.
        services
            .AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();

        JwtSettings settings =
            configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? new JwtSettings();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Claims chegam com o nome do wire (sub/role), sem o remapeamento legado do handler.
                options.MapInboundClaims = false;

                // JWKS direto (não Authority/discovery OIDC — o identity não expõe openid-configuration).
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    settings.JwksUri,
                    new JwksConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = !environment.IsDevelopment() }
                );

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };
            });

        return services;
    }
}
