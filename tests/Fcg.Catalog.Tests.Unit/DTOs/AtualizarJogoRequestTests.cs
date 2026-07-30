using Fcg.Catalog.Application.DTOs;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.DTOs;

public class AtualizarJogoRequestTests
{
    [Fact]
    public void DeveRotularComoUtcADataSemFusoDefinido()
    {
        var request = new AtualizarJogoRequest(
            "Inside",
            60m,
            DataLancamento: new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Unspecified)
        );

        request.DataLancamento!.Value.Kind.Should().Be(DateTimeKind.Utc);
        request.DataLancamento.Should().Be(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void DevePreservarADataJaEmUtc()
    {
        var utc = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var request = new AtualizarJogoRequest("Inside", 60m, DataLancamento: utc);

        request.DataLancamento.Should().Be(utc);
        request.DataLancamento!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void DeveConverterADataLocalParaOInstanteEquivalenteEmUtc()
    {
        var local = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Local);

        var request = new AtualizarJogoRequest("Inside", 60m, DataLancamento: local);

        request.DataLancamento!.Value.Kind.Should().Be(DateTimeKind.Utc);
        request.DataLancamento.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void DeveNormalizarTambemNaCopiaPorWith()
    {
        AtualizarJogoRequest request = new AtualizarJogoRequest("Inside", 60m) with
        {
            DataLancamento = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Unspecified),
        };

        request.DataLancamento!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void DeveAceitarAusenciaDeData()
    {
        var request = new AtualizarJogoRequest("Inside", 60m);

        request.DataLancamento.Should().BeNull();
    }
}
