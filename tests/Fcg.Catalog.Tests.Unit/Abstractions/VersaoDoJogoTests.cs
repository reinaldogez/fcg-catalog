using System.Reflection;
using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Domain.Entities;
using Fcg.Catalog.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.Abstractions;

public class VersaoDoJogoTests
{
    [Fact]
    public void VersaoDeveSerOInstanteDaAlteracaoEmMicrossegundosDesdeAEpoch()
    {
        // 1 s e 123456 µs depois da epoch, na resolução que o banco guarda.
        Jogo jogo = JogoAlteradoEm(DateTime.UnixEpoch.AddTicks(11_234_560));

        VersaoDoJogo.De(jogo).Should().Be(1_123_456);
    }

    [Fact]
    public void VersaoDeveDescartarAFracaoAbaixoDoMicrossegundo()
    {
        Jogo jogo = JogoAlteradoEm(DateTime.UnixEpoch.AddTicks(11_234_567));

        VersaoDoJogo.De(jogo).Should().Be(1_123_456);
    }

    [Fact]
    public void AlteracaoPosteriorDeveProduzirVersaoMaior()
    {
        DateTime instante = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        long anterior = VersaoDoJogo.De(JogoAlteradoEm(instante));
        long posterior = VersaoDoJogo.De(JogoAlteradoEm(instante.AddTicks(10)));

        posterior.Should().Be(anterior + 1);
    }

    // O mesmo instante lido como local não pode deslocar a versão pelo fuso da máquina.
    [Fact]
    public void InstanteLocalDeveProduzirAMesmaVersaoQueOInstanteEmUtc()
    {
        DateTime utc = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        VersaoDoJogo
            .De(JogoAlteradoEm(utc.ToLocalTime()))
            .Should()
            .Be(VersaoDoJogo.De(JogoAlteradoEm(utc)));
    }

    // O instante de alteração só muda por mutação do jogo; aqui ele é fixado direto, no lugar do
    // valor que o banco devolveria na releitura.
    private static Jogo JogoAlteradoEm(DateTime instante)
    {
        var jogo = Jogo.Criar(Titulo.Criar("Hades"), Preco.Criar(90m));

        typeof(Jogo)
            .GetProperty(nameof(Jogo.AtualizadoEm), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(jogo, instante);

        return jogo;
    }
}
