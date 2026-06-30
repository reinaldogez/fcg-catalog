namespace Fcg.Catalog.Application.Options;

// Resource server: valida tokens do identity via JWKS. Sem chave privada — só a URL do JWKS.
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string JwksUri { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
