using System.Security.Claims;
using System.Security.Cryptography;
using Fcg.Catalog.Api.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Emite tokens RS256 válidos para os testes de auth: assina com uma chave RSA fixa e expõe a
// chave pública para o validador. A chave é material de teste — nunca um segredo de runtime.
public static class JwtTestTokens
{
    public const string TestKeyId = "fcg-catalog-test-key";
    public const string TestIssuer = "https://identity.fcg.test";
    public const string TestAudience = "fcg-platform";

    private const string TestRsaPrivateKeyPem = """
        -----BEGIN PRIVATE KEY-----
        MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDFLytbDlx97jk/
        6e0ZG1kwnst3ZIJyjOlrT5B08Rtfgg/3VMagdPXobUsrjtIU7y8PAsSxW4aT9uLl
        yREB657UKxcOtwiPKne7hcQq6INjTQSCz3mNswvPYg+3HXVnlp6Pe29gSdwffUkQ
        pSis1vRDqK4xzaAGlKhTuykQ88/Pbz8l48ER05z1dcYvRB3tKtA1FaRm6rl9nxUT
        5zhUMnJyi7T+pte6B06l1wTqfts2iXpp5MF9wIF6bmkRJUwI/vc3UeZAvpP9Q9kh
        y17JSp95wM5g1EsC3otWfBtOloXD6HD/4eXULC6i4QdzjxfaeRRyTWoYX/BxrebJ
        fpMS6NcJAgMBAAECggEARFpLjy71RPoVgmBWvkNKsZ36LhL7XTYXnAlirAcAVCVD
        35rVl72zmLR6QEkr7fHTwEJXaJuvlQ8aLEl8ycuhrS6auZEdOHOiObDvBWjAUuXZ
        0HaXTlVonKUDiZh+oEAICvjg7OHtmkTV1R1Lck65MdMBP2ZmOKHDj/LrE2NRADiW
        JiA0BMwO6K95tig3tkjaot0IFsnAPd3gXpZ+p+JDc7CnQb+DHfndlTKVgB9zoZgn
        hbKBiAmTOMdjnhNSUCykNU6m6TwSiHq8JY6zxq3Tt4VxI5uDZJ8O0AWsNWb8qIim
        E/RW5dNQJnjsJhb/pxQkiFB6y/FVUlwB7aOpOAbi2QKBgQDqQPtE8JLUQRBKeTHy
        5ExEJ0FC8FcXdwEov5SwPSwn1wq6oGD4ZUZWEnZ+RtASTTz6aBKiX+vOho30YhIq
        7pGhfdsrR/19oSQQlj0D5rUgukIIbP1+B/z4iq/B7VmMSu7iNPQYxX4CxFALqvJR
        fbE8TL0yx7q+cFtIBmO5EevC/wKBgQDXfTurBL4d3UZJjCzUwbDDf6sxzI7SJaY2
        1QXHR6wJlpEmJ0vKODAm5nKAtEJn/VY5OC4gh75CpVPozTJFdh7EETD/cOOSZfst
        CQLVsn567I55Z4PgBb0uprJQ6pD+E1cDPE8P2xFbmwhmsE3rjETRRIpjksSZEjuy
        VBGaBjdN9wKBgF5KwdnCLJgbiH8xwZVPqBNW6cIUFDpxwJmyZBt8xCVVPhBZNi9G
        NMW4sNGrl12GkaEJ+1Y43iZHqyRPxZhaZ2xlyK7nT3YeQvIaR30mhIoj7yiNFoyA
        kqdIy+53p6/9CaMsRYUjGdHrS30m1ltPCOSIzy99jgHSICwhxkpcmFXpAoGBANLc
        5wBoeak7l8XsdwoSJuiHC3yFkNQup0FMnoTsq3oObiaJmW5eITBPnIg43CpqCm+f
        e/O1IQaSRVOvR5wVA/IUaH/tdaMSTAE7qhx2t7GNvbUrCC61LvRxhlgL0KnvPtwZ
        rbv0QD4FrOjfaMAv2D929HyPZ/Xpk6TjAv5XStLtAoGAM3E55Ja1547Alte/87bP
        VU+NB5BE0yu4vBC4dhzXkPnGJph22O6T1r8kaUPPBhjf4yOB0B4yacEoGcFFPBu1
        +o1/pzwWoEUZEXrNHhHBQ12V8HuvaNREkBFI398P6EuzxG8acOS6vUQjgRzvsE9U
        QRGXfAvorKFVe6GygpWYr+8=
        -----END PRIVATE KEY-----
        """;

    private static readonly RSA s_rsa = CriarRsa();

    private static readonly RsaSecurityKey s_signingKey = new(s_rsa) { KeyId = TestKeyId };

    // Chave pública (sem material privado) que a fixture injeta no validador — faz o papel do JWKS.
    public static readonly RsaSecurityKey PublicSecurityKey = new(s_rsa.ExportParameters(false))
    {
        KeyId = TestKeyId,
    };

    public static string TokenAdmin(Guid? sub = null, string? email = null, string? nome = null) =>
        GenerateToken(
            sub ?? Guid.NewGuid(),
            email ?? "admin@fcg.test",
            nome ?? "Admin FCG",
            AuthorizationPolicies.RoleAdmin
        );

    public static string TokenUsuario(Guid sub, string? email = null, string? nome = null) =>
        GenerateToken(sub, email ?? "usuario@fcg.test", nome ?? "Usuário FCG", role: null);

    public static string GenerateToken(Guid sub, string email, string nome, string? role)
    {
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, sub.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, nome),
        ];
        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(s_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static RSA CriarRsa()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(TestRsaPrivateKeyPem);
        return rsa;
    }
}
