using System.Collections.Concurrent;
using Serilog;

namespace Fcg.Catalog.Tests.Integration.Fixtures;

// Propriedades que a borda HTTP acrescentou à linha única do log de requisição, na ordem em que
// chegaram.
public sealed class RegistroDeDiagnostico
{
    private readonly ConcurrentQueue<(string Propriedade, object? Valor)> _propriedades = new();

    public IReadOnlyList<object?> ValoresDe(string propriedade) =>
        [.. _propriedades.Where(p => p.Propriedade == propriedade).Select(p => p.Valor)];

    public void Limpar() => _propriedades.Clear();

    internal void Registrar(string propriedade, object? valor) =>
        _propriedades.Enqueue((propriedade, valor));
}

// Envolve o contexto de diagnóstico real em vez de substituí-lo: a propriedade continua chegando
// ao log de requisição do Serilog no host de teste, e o teste ainda enxerga o que foi registrado.
// Capturar o log emitido exigiria trocar o logger estático do Serilog, que é global ao processo e
// ficaria à mercê das outras coleções da suíte, que correm em paralelo com esta.
public sealed class DiagnosticContextEspiao(
    IDiagnosticContext interno,
    RegistroDeDiagnostico registro
) : IDiagnosticContext
{
    public void Set(string propertyName, object? value, bool destructureObjects = false)
    {
        registro.Registrar(propertyName, value);
        interno.Set(propertyName, value, destructureObjects);
    }

    public void SetException(Exception exception) => interno.SetException(exception);
}
