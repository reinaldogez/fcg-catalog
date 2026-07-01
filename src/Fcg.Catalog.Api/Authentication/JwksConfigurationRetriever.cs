using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fcg.Catalog.Api.Authentication;

// O identity expõe JWKS cru (/.well-known/jwks.json), mas não um documento de discovery OIDC
// (/.well-known/openid-configuration). O retriever OIDC padrão espera o discovery e não extrairia
// chave nenhuma de um JWKS — então este retriever lê o JWKS direto e devolve uma configuração só
// com as signing keys. Envolvido num ConfigurationManager, ganha cache e rotação de chave de graça.
public sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel
    )
    {
        string documento = await retriever.GetDocumentAsync(address, cancel);
        JsonWebKeySet jwks = new(documento);

        OpenIdConnectConfiguration configuracao = new();
        foreach (SecurityKey chave in jwks.GetSigningKeys())
            configuracao.SigningKeys.Add(chave);

        return configuracao;
    }
}
