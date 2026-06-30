using Fcg.Catalog.Application.UseCases.Biblioteca;
using Fcg.Catalog.Application.UseCases.Jogos;
using Fcg.Catalog.Application.UseCases.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace Fcg.Catalog.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CriarJogoUseCase>();
        services.AddScoped<ObterJogoPorIdUseCase>();
        services.AddScoped<ListarJogosUseCase>();
        services.AddScoped<AtualizarJogoUseCase>();
        services.AddScoped<DesativarJogoUseCase>();

        services.AddScoped<CriarPedidoUseCase>();
        services.AddScoped<ObterPedidoPorIdUseCase>();
        services.AddScoped<AprovarPedidoUseCase>();
        services.AddScoped<RejeitarPedidoUseCase>();

        services.AddScoped<ObterBibliotecaDoUsuarioUseCase>();

        return services;
    }
}
