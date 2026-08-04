using Fcg.Catalog.Application.Abstractions;
using Fcg.Catalog.Application.UseCases.Biblioteca;
using Fcg.Catalog.Domain.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.UseCases.Biblioteca;

public class ProjetarItemBibliotecaUseCaseTests
{
    private readonly Mock<IProjecaoBiblioteca> _projecaoBiblioteca = new();
    private readonly ProjetarItemBibliotecaUseCase _useCase;

    public ProjetarItemBibliotecaUseCaseTests() =>
        _useCase = new ProjetarItemBibliotecaUseCase(_projecaoBiblioteca.Object);

    [Fact]
    public async Task DeveProjetarOItemComOsCamposRecebidos()
    {
        var usuarioId = Guid.NewGuid();
        var jogoId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        var adquiridoEm = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        await _useCase.ExecutarAsync(
            usuarioId,
            jogoId,
            pedidoId,
            "Hollow Knight",
            19.90m,
            adquiridoEm,
            CancellationToken.None
        );

        _projecaoBiblioteca.Verify(
            p =>
                p.ProjetarAsync(
                    new ItemBibliotecaProjetado(
                        usuarioId,
                        jogoId,
                        pedidoId,
                        "Hollow Knight",
                        19.90m,
                        adquiridoEm
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public void NaoDeveDependerDeUnitOfWork()
    {
        // A escrita da projeção não é transacional no banco relacional, e a fila que a alimenta não
        // tem Inbox: um IUnitOfWork aqui só poderia comitar trabalho de outra pessoa. Guarda
        // estrutural, no molde da que protege os use cases do consumer de crédito.
        bool dependeDeUnitOfWork = typeof(ProjetarItemBibliotecaUseCase)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(IUnitOfWork));

        dependeDeUnitOfWork.Should().BeFalse();
    }
}
