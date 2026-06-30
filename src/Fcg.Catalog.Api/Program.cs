using System.Text.Json.Serialization;
using Fcg.Catalog.Api.Health;
using Fcg.Catalog.Api.Middleware;
using Fcg.Catalog.Application;
using Fcg.Catalog.Infrastructure;
using Fcg.Catalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Log estruturado no console. Os sinks de stack (OTLP/Loki) entram numa etapa de observabilidade.
builder.Services.AddSerilog(config => config.Enrich.FromLogContext().WriteTo.Console());

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

builder.Services.AddOpenApi();

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

app.UseSerilogRequestLogging();

// Cedo no pipeline: traduz qualquer exceção de domínio para problem+json antes do resto.
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseRateLimiter();

// Autenticação e autorização entram numa etapa posterior.

app.MapControllers();
app.MapCatalogHealthChecks();

app.Run();

// Expõe o entrypoint ao WebApplicationFactory<Program> dos testes de Integration.
public partial class Program;
