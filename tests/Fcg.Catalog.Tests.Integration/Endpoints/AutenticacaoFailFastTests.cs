using Fcg.Catalog.Application.Options;
using Fcg.Catalog.Domain.Interfaces;
using Fcg.Catalog.Domain.Services;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Catalog.Tests.Integration.Endpoints;

// Fora da coleção de Integration: sobe um host próprio (sem Postgres) só para provar o fail-fast.
public class AutenticacaoFailFastTests
{
    [Fact]
    public void StartupSemConfigDeJwtDeveFalharNoStart()
    {
        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                // Passa o fail-fast de connection string; o alvo aqui é a validação do JwtSettings.
                builder.UseSetting(
                    "ConnectionStrings:Catalog",
                    "Host=localhost;Database=fail;Username=u;Password=p"
                );
                builder.UseSetting("Jwt:JwksUri", "");
                builder.UseSetting("Jwt:Issuer", "");
                builder.UseSetting("Jwt:Audience", "");

                // Serviços de mensageria só entram numa task futura; providos aqui para a validação
                // de DI passar e o start alcançar o ValidateOnStart do JwtSettings.
                builder.ConfigureTestServices(services =>
                {
                    services.AddScoped<IPedidoDomainService, PedidoDomainService>();
                    services.AddSingleton<IPublishEndpoint, PublishEndpointNoop>();
                });
            });

        // CreateClient dispara o start do host — e com ele o ValidateOnStart do JwtSettings.
        Action start = () => factory.CreateClient();

        // A exceção pode vir agregada pelo host; basta que a falha aponte a config ausente.
        start
            .Should()
            .Throw<Exception>()
            .Which.ToString()
            .Should()
            .Contain(nameof(JwtSettings.JwksUri));
    }
}
