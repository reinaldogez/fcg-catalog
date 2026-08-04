namespace Fcg.Catalog.Application.DTOs;

internal static class InstanteDeEntrada
{
    // A coluna é timestamptz e recusa DateTime com Kind Unspecified, que é justamente como chega
    // uma data JSON sem sufixo de fuso. Valor com deslocamento chega como Local: esse precisa ser
    // convertido, não reetiquetado, senão o instante muda.
    internal static DateTime? ParaUtc(DateTime? valor)
    {
        if (valor is null)
            return null;

        return valor.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(valor.Value, DateTimeKind.Utc)
            : valor.Value.ToUniversalTime();
    }
}
