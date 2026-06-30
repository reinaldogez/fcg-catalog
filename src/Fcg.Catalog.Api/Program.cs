using Fcg.Catalog.Infrastructure;

// Composição mínima: registra só a persistência, o suficiente para o
// WebApplicationFactory dos testes de Integration exercitar os repositórios.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

app.Run();

// Expõe o entrypoint ao WebApplicationFactory<Program> dos testes de Integration.
public partial class Program;
