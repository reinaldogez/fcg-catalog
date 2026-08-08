using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

namespace Fcg.Catalog.Api.Observability;

public static class ObservabilityModule
{
    private const string ServiceName = "Fcg.Catalog.Api";
    private const string AppLabel = "fcg-catalog";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        // Variavel oficial do OpenTelemetry, lida direta (nao via GetSection); o Loki e push
        // direto do Serilog por Loki:Url. Ambos config-gated: ausencia desliga, presenca liga.
        string? lokiUrl = builder.Configuration["Loki:Url"];
        string? otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        // Console e enricher de trace entram sempre; o sink Loki so com URL configurada.
        builder.Host.UseSerilog(
            (_, loggerConfig) =>
            {
                loggerConfig
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithSpan()
                    .Enrich.WithProperty("service.name", ServiceName)
                    .WriteTo.Console();

                // Label app=fcg-catalog como label de stream (nao propriedade) — e o que faz
                // {app="fcg-catalog"} e o agregado {app=~"fcg-.*"} resolverem no Grafana.
                if (!string.IsNullOrWhiteSpace(lokiUrl))
                {
                    loggerConfig.WriteTo.GrafanaLoki(
                        lokiUrl,
                        labels: [new LokiLabel { Key = "app", Value = AppLabel }]
                    );
                }
            }
        );

        // OTLP (traces/metrics) so com endpoint configurado. Instrumenta HTTP de entrada
        // (AspNetCore), HTTP de saida (JWKS do identity), o bus — MassTransit como source
        // (trace bilateral: encadeia publish e consume pelo TraceId via headers AMQP) e meter —
        // mais o banco e o cache.
        if (!string.IsNullOrWhiteSpace(otelEndpoint))
        {
            builder
                .Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(ServiceName))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddSource("MassTransit");

                    // Chamada pelo tipo declarante: como extensão, o nome colide com o
                    // AddNpgsql do EF Core, que registra contexto e exige connection string.
                    Npgsql.TracerProviderBuilderExtensions.AddNpgsql(tracing);

                    // A conexão vem do container — é a mesma que o cache distribuído usa, e o
                    // profiler só enxerga as chamadas feitas por ela.
                    //
                    // Texto do comando fora do span de propósito, nas duas instrumentações: o
                    // padrão do Npgsql já é não emitir, e aqui o desligamento é explícito
                    // porque o comando carrega valor de chave e de argumento.
                    tracing.AddRedisInstrumentation(options =>
                        options.SetVerboseDatabaseStatements = false
                    );

                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otelEndpoint));
                })
                .WithMetrics(metrics =>
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddMeter("MassTransit")
                        .AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otelEndpoint))
                );
        }

        return builder;
    }
}
