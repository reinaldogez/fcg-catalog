using System.Text.Json.Serialization;
using Fcg.Catalog.Api.Authentication;
using Fcg.Catalog.Api.Authorization;
using Fcg.Catalog.Api.Health;
using Fcg.Catalog.Api.Middleware;
using Fcg.Catalog.Api.Observability;
using Fcg.Catalog.Api.OpenApi;
using Fcg.Catalog.Application;
using Fcg.Catalog.Infrastructure;
using Fcg.Catalog.Infrastructure.Persistence;
using Fcg.Catalog.Infrastructure.Seed;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Serilog + OpenTelemetry: console sempre; OTLP e sink Loki config-gated (sobem so com
// OTEL_EXPORTER_OTLP_ENDPOINT / Loki:Url). Registrado antes do Build para valer tambem no Job.
builder.AddObservability();

// Fail-fast: a API depende de PostgreSQL; sem connection string não há boot válido.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Catalog")))
    throw new InvalidOperationException("Connection string 'Catalog' não configurada.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder
    // Mantém o sufixo Async no nome da action para casar com nameof(...) no link generation.
    .Services.AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())
    );

builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>()
);

builder.Services.AddCatalogAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCatalogAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(
        "fixed",
        limiter =>
        {
            limiter.PermitLimit = 100;
            limiter.Window = TimeSpan.FromSeconds(1);
            limiter.QueueLimit = 0;
        }
    );
});

builder.Services.AddHealthChecks().AddDbContextCheck<CatalogDbContext>("postgres", tags: ["ready"]);

WebApplication app = builder.Build();

// Modo Job: o mesmo binário aplica migrations e/ou semeia e encerra sem subir o host web.
// Flags independentes e combináveis; ordem migrate→seed forçada aqui, não pela ordem dos args.
if (args.Contains("--migrate") || args.Contains("--seed"))
{
    using IServiceScope scope = app.Services.CreateScope();

    if (args.Contains("--migrate"))
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();

    if (args.Contains("--seed"))
        await scope.ServiceProvider.GetRequiredService<CatalogSeeder>().SeedAsync();

    return;
}

app.UseSerilogRequestLogging();

// Cedo no pipeline: traduz qualquer exceção de domínio para problem+json antes do resto.
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapCatalogHealthChecks();

app.Run();

// Expõe o entrypoint ao WebApplicationFactory<Program> dos testes de Integration.
public partial class Program;
