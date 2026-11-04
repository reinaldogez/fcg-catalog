using System.Text.Json;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using Fcg.Catalog.Infrastructure.Busca;
using Fcg.Catalog.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenSearch.Net;
using Xunit;
using MetodoHttp = OpenSearch.Net.HttpMethod;

namespace Fcg.Catalog.Tests.Integration.Busca;

// As portas do índice contra um OpenSearch real, compostas pela mesma extensão da aplicação. Cada
// teste parte de um cluster sem o índice e sem o bloqueio de criação automática, para que o
// bootstrap seja exercitado sempre a partir do zero.
public sealed class IndiceDeJogosTests(OpenSearchFixture fixture)
    : IClassFixture<OpenSearchFixture>,
        IAsyncLifetime,
        IDisposable
{
    private const string Indice = MapeamentoDoIndice.NomeDoIndice;

    private readonly ServiceProvider _provider = Compor(fixture.Endpoint);

    private IIndiceDeJogos Escrita => _provider.GetRequiredService<IIndiceDeJogos>();

    private IPreparacaoDoIndice Preparacao => _provider.GetRequiredService<IPreparacaoDoIndice>();

    public async Task InitializeAsync()
    {
        await RequisitarAsync(MetodoHttp.DELETE, Indice);
        await RequisitarAsync(
            MetodoHttp.PUT,
            "_cluster/settings",
            """{ "persistent": { "action.auto_create_index": null } }"""
        );
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task BootstrapDeveBloquearCriacaoAutomaticaECriarOIndiceComMapeamentoEstrito()
    {
        (await Preparacao.EstaSaudavelAsync()).Should().BeTrue();

        await Preparacao.GarantirAsync();

        using JsonDocument cluster = await ObterJsonAsync("_cluster/settings");
        cluster
            .RootElement.GetProperty("persistent")
            .GetProperty("action")
            .GetProperty("auto_create_index")
            .GetString()
            .Should()
            .Be("-jogos,+*");

        using JsonDocument settings = await ObterJsonAsync($"{Indice}/_settings");
        JsonElement indice = settings
            .RootElement.GetProperty(Indice)
            .GetProperty("settings")
            .GetProperty("index");
        indice.GetProperty("number_of_shards").GetString().Should().Be("1");
        indice.GetProperty("number_of_replicas").GetString().Should().Be("0");

        JsonElement analisador = indice
            .GetProperty("analysis")
            .GetProperty("analyzer")
            .GetProperty("texto_do_catalogo");
        analisador.GetProperty("tokenizer").GetString().Should().Be("standard");
        analisador
            .GetProperty("filter")
            .EnumerateArray()
            .Select(f => f.GetString())
            .Should()
            .Equal("lowercase", "asciifolding");

        await AssertarMapeamentoAsync();
    }

    [Fact]
    public async Task BootstrapRepetidoEEmParaleloNaoDeveFalharNemAlterarOMapeamento()
    {
        await Task.WhenAll(Preparacao.GarantirAsync(), Preparacao.GarantirAsync());
        await Preparacao.GarantirAsync();

        await AssertarMapeamentoAsync();
    }

    // Sem o bloqueio, a remoção com versão externa criaria o índice com mapeamento dinâmico.
    [Fact]
    public async Task EscritaComIndiceAusenteNaoDeveCriarOIndice()
    {
        await Preparacao.GarantirAsync();
        await RequisitarAsync(MetodoHttp.DELETE, Indice);
        Jogo jogo = NovoJogo("Indice ausente");

        (await Escrita.GravarAsync(DocumentoJogo.De(jogo), VersaoDoJogo.De(jogo)))
            .Should()
            .Be(ResultadoEscritaIndice.IndiceAusente);
        (await Escrita.RemoverAsync(jogo.Id, VersaoDoJogo.De(jogo)))
            .Should()
            .Be(ResultadoEscritaIndice.IndiceAusente);
        (await Escrita.RemoverAsync(jogo.Id, versao: null))
            .Should()
            .Be(ResultadoEscritaIndice.IndiceAusente);

        StringResponse existe = await RequisitarAsync(MetodoHttp.HEAD, Indice);
        existe.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DocumentoDeveTerSoOsSeisCamposEAVersaoEmMicrossegundos()
    {
        await Preparacao.GarantirAsync();
        Jogo jogo = NovoJogo(
            "Documento completo",
            descricao: "Exploração em mundo aberto",
            desenvolvedora: "Estúdio Teste",
            dataLancamento: new DateTime(2020, 3, 15, 0, 0, 0, DateTimeKind.Utc)
        );
        long versao = VersaoDoJogo.De(jogo);

        (await Escrita.GravarAsync(DocumentoJogo.De(jogo), versao))
            .Should()
            .Be(ResultadoEscritaIndice.Aplicada);

        using JsonDocument documento = await ObterJsonAsync($"{Indice}/_doc/{jogo.Id}");
        documento.RootElement.GetProperty("_version").GetInt64().Should().Be(versao);

        JsonElement fonte = documento.RootElement.GetProperty("_source");
        fonte
            .EnumerateObject()
            .Select(campo => campo.Name)
            .Should()
            .BeEquivalentTo(
                "id",
                "titulo",
                "descricao",
                "preco",
                "desenvolvedora",
                "dataLancamento"
            );
        fonte.GetProperty("id").GetString().Should().Be(jogo.Id.ToString());
        fonte.GetProperty("titulo").GetString().Should().Be("Documento completo");
        fonte.GetProperty("desenvolvedora").GetString().Should().Be("Estúdio Teste");
        fonte.GetProperty("dataLancamento").GetDateTime().Should().Be(new DateTime(2020, 3, 15));

        await fixture.Inspecao.Indices.RefreshAsync(Indice);
        (await Preparacao.ContarAsync()).Should().Be(1);
    }

    [Fact]
    public async Task VersaoMenorDeveSerDescartadaSemAlterarODocumento()
    {
        await Preparacao.GarantirAsync();
        Jogo jogo = NovoJogo("Estado novo");
        var novo = DocumentoJogo.De(jogo);

        (await Escrita.GravarAsync(novo, 2_000)).Should().Be(ResultadoEscritaIndice.Aplicada);
        (await Escrita.GravarAsync(novo with { Titulo = "Estado velho" }, 1_000))
            .Should()
            .Be(ResultadoEscritaIndice.Obsoleta);

        using JsonDocument documento = await ObterJsonAsync($"{Indice}/_doc/{jogo.Id}");
        documento.RootElement.GetProperty("_version").GetInt64().Should().Be(2_000);
        documento
            .RootElement.GetProperty("_source")
            .GetProperty("titulo")
            .GetString()
            .Should()
            .Be("Estado novo");
    }

    // Reentrega e reconstrução reescrevem o mesmo estado: não pode virar conflito.
    [Fact]
    public async Task MesmaVersaoDeveSerAceitaSemErro()
    {
        await Preparacao.GarantirAsync();
        var documento = DocumentoJogo.De(NovoJogo("Mesma versão"));

        (await Escrita.GravarAsync(documento, 5_000)).Should().Be(ResultadoEscritaIndice.Aplicada);
        (await Escrita.GravarAsync(documento, 5_000)).Should().Be(ResultadoEscritaIndice.Aplicada);
    }

    [Fact]
    public async Task RemocaoVersionadaDeveRespeitarAVersaoGravada()
    {
        await Preparacao.GarantirAsync();
        Jogo jogo = NovoJogo("Remoção versionada");
        await Escrita.GravarAsync(DocumentoJogo.De(jogo), 3_000);

        (await Escrita.RemoverAsync(jogo.Id, 2_999)).Should().Be(ResultadoEscritaIndice.Obsoleta);
        (await RequisitarAsync(MetodoHttp.GET, $"{Indice}/_doc/{jogo.Id}"))
            .HttpStatusCode.Should()
            .Be(200);

        (await Escrita.RemoverAsync(jogo.Id, 3_000)).Should().Be(ResultadoEscritaIndice.Aplicada);
        (await RequisitarAsync(MetodoHttp.GET, $"{Indice}/_doc/{jogo.Id}"))
            .HttpStatusCode.Should()
            .Be(404);
    }

    [Fact]
    public async Task RemocaoDeDocumentoInexistenteDeveSerAplicada()
    {
        await Preparacao.GarantirAsync();

        (await Escrita.RemoverAsync(Guid.NewGuid(), 1_000))
            .Should()
            .Be(ResultadoEscritaIndice.Aplicada);
        (await Escrita.RemoverAsync(Guid.NewGuid(), versao: null))
            .Should()
            .Be(ResultadoEscritaIndice.Aplicada);
    }

    [Fact]
    public async Task RemocaoSemVersaoDeveRemoverODocumento()
    {
        await Preparacao.GarantirAsync();
        Jogo jogo = NovoJogo("Remoção sem versão");
        await Escrita.GravarAsync(DocumentoJogo.De(jogo), 4_000);

        (await Escrita.RemoverAsync(jogo.Id, versao: null))
            .Should()
            .Be(ResultadoEscritaIndice.Aplicada);
        (await RequisitarAsync(MetodoHttp.GET, $"{Indice}/_doc/{jogo.Id}"))
            .HttpStatusCode.Should()
            .Be(404);
    }

    [Fact]
    public async Task PrecoDeveVoltarExatoNoDocumento()
    {
        await Preparacao.GarantirAsync();
        Jogo jogo = NovoJogo("Preço exato", preco: 19.90m);

        await Escrita.GravarAsync(DocumentoJogo.De(jogo), VersaoDoJogo.De(jogo));

        using JsonDocument documento = await ObterJsonAsync($"{Indice}/_doc/{jogo.Id}");
        documento
            .RootElement.GetProperty("_source")
            .GetProperty("preco")
            .GetDecimal()
            .Should()
            .Be(19.90m);
    }

    [Fact]
    public async Task CampoForaDoMapeamentoDeveSerRecusado()
    {
        await Preparacao.GarantirAsync();
        var id = Guid.NewGuid();

        StringResponse resposta = await RequisitarAsync(
            MetodoHttp.PUT,
            $"{Indice}/_doc/{id}",
            $$"""{ "id": "{{id}}", "titulo": "Campo extra", "preco": 10, "genero": "rpg" }"""
        );

        resposta.HttpStatusCode.Should().Be(400);
        resposta.Body.Should().Contain("strict_dynamic_mapping_exception");
    }

    [Fact]
    public async Task ClusterInalcancavelNaoDeveSerSaudavelNemLancar()
    {
        await using ServiceProvider inalcancavel = Compor("http://127.0.0.1:1");

        bool saudavel = await inalcancavel
            .GetRequiredService<IPreparacaoDoIndice>()
            .EstaSaudavelAsync();

        saudavel.Should().BeFalse();
    }

    private async Task AssertarMapeamentoAsync()
    {
        using JsonDocument mapeamento = await ObterJsonAsync($"{Indice}/_mapping");
        JsonElement raiz = mapeamento.RootElement.GetProperty(Indice).GetProperty("mappings");

        raiz.GetProperty("dynamic").GetString().Should().Be("strict");

        JsonElement campos = raiz.GetProperty("properties");
        campos
            .EnumerateObject()
            .Select(campo => campo.Name)
            .Should()
            .BeEquivalentTo(
                "id",
                "titulo",
                "descricao",
                "preco",
                "desenvolvedora",
                "dataLancamento"
            );

        campos.GetProperty("id").GetProperty("type").GetString().Should().Be("keyword");

        foreach (string texto in new[] { "titulo", "descricao", "desenvolvedora" })
        {
            JsonElement campo = campos.GetProperty(texto);
            campo.GetProperty("type").GetString().Should().Be("text");
            campo.GetProperty("analyzer").GetString().Should().Be("texto_do_catalogo");
        }

        JsonElement preco = campos.GetProperty("preco");
        preco.GetProperty("type").GetString().Should().Be("scaled_float");
        preco.GetProperty("scaling_factor").GetDouble().Should().Be(100);
        preco.GetProperty("index").GetBoolean().Should().BeFalse();

        JsonElement data = campos.GetProperty("dataLancamento");
        data.GetProperty("type").GetString().Should().Be("date");
        data.GetProperty("index").GetBoolean().Should().BeFalse();
    }

    private async Task<JsonDocument> ObterJsonAsync(string caminho)
    {
        StringResponse resposta = await RequisitarAsync(MetodoHttp.GET, caminho);

        resposta.Success.Should().BeTrue(resposta.Body);

        return JsonDocument.Parse(resposta.Body);
    }

    private Task<StringResponse> RequisitarAsync(
        MetodoHttp metodo,
        string caminho,
        string? corpo = null
    ) =>
        fixture.Inspecao.LowLevel.DoRequestAsync<StringResponse>(
            metodo,
            caminho,
            CancellationToken.None,
            corpo is null ? null : PostData.String(corpo)
        );

    private static Jogo NovoJogo(
        string titulo,
        decimal preco = 59.90m,
        string? descricao = null,
        string? desenvolvedora = null,
        DateTime? dataLancamento = null
    ) =>
        Jogo.Criar(
            Titulo.Criar(titulo),
            Preco.Criar(preco),
            descricao,
            desenvolvedora,
            dataLancamento
        );

    private static ServiceProvider Compor(string endpoint)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCatalogSearch(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [OpenSearchSettings.ChaveEndpoint] = endpoint,
                    }
                )
                .Build()
        );

        return services.BuildServiceProvider();
    }
}
