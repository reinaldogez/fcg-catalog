using Microsoft.Extensions.Options;

namespace Fcg.Catalog.Application.Options;

// Fail-fast: sem JwksUri/Issuer/Audience o resource server não consegue validar tokens.
public class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        List<string> falhas = [];

        if (string.IsNullOrWhiteSpace(options.JwksUri))
            falhas.Add($"'{nameof(JwtSettings.JwksUri)}' é obrigatório.");

        if (string.IsNullOrWhiteSpace(options.Issuer))
            falhas.Add($"'{nameof(JwtSettings.Issuer)}' é obrigatório.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            falhas.Add($"'{nameof(JwtSettings.Audience)}' é obrigatório.");

        return falhas.Count > 0
            ? ValidateOptionsResult.Fail(falhas)
            : ValidateOptionsResult.Success;
    }
}
