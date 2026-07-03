using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fcg.Catalog.Application.DTOs;
using Fcg.Catalog.Tests.Bdd.Support;
using Fcg.Catalog.Tests.Integration.Fixtures;
using Fcg.Contracts.Enums;
using Fcg.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Reqnroll;

namespace Fcg.Catalog.Tests.Bdd.Steps;

// O passo de pagamento publica o PaymentProcessedEvent direto no bus de teste — faz o papel do
// serviço de pagamentos, que não participa deste showcase. O fechamento da saga é assíncrono
// (consumer), então os "Entao" observam o efeito por HTTP com poll até o timeout.
[Binding]
public class FluxoDeCompraSteps(HttpClient client, CenarioEstado estado, IBus bus)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    [Given(@"que o administrador cadastrou o jogo ""([^""]*)"" com preco ([\d.]+)")]
    public async Task DadoQueOAdministradorCadastrouOJogoComPreco(string titulo, string preco)
    {
        AutenticarComo(JwtTestTokens.TokenAdmin());
        var request = new CriarJogoRequest(
            titulo,
            decimal.Parse(preco, CultureInfo.InvariantCulture)
        );
        HttpResponseMessage resposta = await client.PostAsJsonAsync("/api/jogos", request);
        ((int)resposta.StatusCode)
            .Should()
            .Be(201, $"pré-condição: o cadastro do jogo '{titulo}' deveria ter retornado 201");

        JogoResponse? jogo = await resposta.Content.ReadFromJsonAsync<JogoResponse>(s_jsonOptions);
        jogo.Should().NotBeNull();
        estado.JogoId = jogo!.Id;
    }

    [When(@"o usuario cria um pedido para o jogo")]
    public async Task QuandoOUsuarioCriaUmPedidoParaOJogo()
    {
        AutenticarComo(JwtTestTokens.TokenUsuario(estado.UsuarioId));
        var request = new CriarPedidoRequest(estado.JogoId);
        estado.UltimaResposta = await client.PostAsJsonAsync("/api/pedidos", request);

        if (estado.UltimaResposta.IsSuccessStatusCode)
        {
            PedidoResponse? pedido =
                await estado.UltimaResposta.Content.ReadFromJsonAsync<PedidoResponse>(
                    s_jsonOptions
                );
            pedido.Should().NotBeNull();
            estado.PedidoId = pedido!.Id;
        }
    }

    [Given(@"o usuario criou um pedido para o jogo")]
    public async Task DadoOUsuarioCriouUmPedidoParaOJogo()
    {
        await QuandoOUsuarioCriaUmPedidoParaOJogo();
        ((int)estado.UltimaResposta!.StatusCode)
            .Should()
            .Be(202, "pré-condição: a criação do pedido deveria ter sido aceita");
    }

    [When(@"o pagamento do pedido e processado como aprovado")]
    public async Task QuandoOPagamentoDoPedidoEProcessadoComoAprovado() =>
        await PublicarPagamentoAsync(PaymentStatus.Approved, motivo: null);

    [When(@"o pagamento do pedido e processado como rejeitado com motivo ""([^""]*)""")]
    public async Task QuandoOPagamentoDoPedidoEProcessadoComoRejeitadoComMotivo(string motivo) =>
        await PublicarPagamentoAsync(PaymentStatus.Rejected, motivo);

    [Then(@"o pedido fica com status ""([^""]*)""")]
    public async Task EntaoOPedidoFicaComStatus(string statusEsperado)
    {
        PedidoResponse? pedido = await AguardarPedidoAsync(p => p.Status == statusEsperado);
        pedido
            .Should()
            .NotBeNull($"o pedido deveria chegar ao status '{statusEsperado}' dentro do timeout");
    }

    [Then(@"o pedido fica com status ""([^""]*)"" e motivo ""([^""]*)""")]
    public async Task EntaoOPedidoFicaComStatusEMotivo(string statusEsperado, string motivoEsperado)
    {
        PedidoResponse? pedido = await AguardarPedidoAsync(p => p.Status == statusEsperado);
        pedido
            .Should()
            .NotBeNull($"o pedido deveria chegar ao status '{statusEsperado}' dentro do timeout");
        pedido!.MotivoRecusa.Should().Be(motivoEsperado);
    }

    [Then(@"o jogo aparece na biblioteca do usuario")]
    public async Task EntaoOJogoApareceNaBibliotecaDoUsuario()
    {
        IReadOnlyList<ItemBibliotecaResponse> itens = await ObterBibliotecaAsync();
        itens.Should().ContainSingle(item => item.JogoId == estado.JogoId);
    }

    [Then(@"a biblioteca do usuario nao contem o jogo")]
    public async Task EntaoABibliotecaDoUsuarioNaoContemOJogo()
    {
        IReadOnlyList<ItemBibliotecaResponse> itens = await ObterBibliotecaAsync();
        itens.Should().NotContain(item => item.JogoId == estado.JogoId);
    }

    [When(@"outro usuario consulta esse pedido")]
    public async Task QuandoOutroUsuarioConsultaEssePedido()
    {
        AutenticarComo(JwtTestTokens.TokenUsuario(Guid.NewGuid()));
        estado.UltimaResposta = await client.GetAsync($"/api/pedidos/{estado.PedidoId}");
    }

    private void AutenticarComo(string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task PublicarPagamentoAsync(PaymentStatus status, string? motivo) =>
        await bus.Publish(
            new PaymentProcessedEvent
            {
                OrderId = estado.PedidoId,
                UserId = estado.UsuarioId,
                GameId = estado.JogoId,
                Status = status,
                RejectionReason = motivo,
            }
        );

    private async Task<IReadOnlyList<ItemBibliotecaResponse>> ObterBibliotecaAsync()
    {
        AutenticarComo(JwtTestTokens.TokenUsuario(estado.UsuarioId));
        HttpResponseMessage resposta = await client.GetAsync($"/api/biblioteca/{estado.UsuarioId}");
        ((int)resposta.StatusCode).Should().Be(200);

        IReadOnlyList<ItemBibliotecaResponse>? itens = await resposta.Content.ReadFromJsonAsync<
            IReadOnlyList<ItemBibliotecaResponse>
        >(s_jsonOptions);
        itens.Should().NotBeNull();
        return itens!;
    }

    // Poll no GET do pedido (o polling é o contrato do 202) até a condição valer ou o timeout
    // estourar; devolve null no timeout para a asserção do chamador falhar com contexto.
    private async Task<PedidoResponse?> AguardarPedidoAsync(Func<PedidoResponse, bool> condicao)
    {
        AutenticarComo(JwtTestTokens.TokenUsuario(estado.UsuarioId));
        DateTime limite = DateTime.UtcNow + s_timeout;
        while (DateTime.UtcNow < limite)
        {
            HttpResponseMessage resposta = await client.GetAsync($"/api/pedidos/{estado.PedidoId}");
            if (resposta.IsSuccessStatusCode)
            {
                PedidoResponse? pedido = await resposta.Content.ReadFromJsonAsync<PedidoResponse>(
                    s_jsonOptions
                );
                if (pedido is not null && condicao(pedido))
                    return pedido;
            }
            await Task.Delay(200);
        }
        return null;
    }
}
