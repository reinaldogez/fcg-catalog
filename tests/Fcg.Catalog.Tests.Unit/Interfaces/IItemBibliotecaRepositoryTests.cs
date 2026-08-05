using Fcg.Catalog.Domain.Interfaces;
using FluentAssertions;
using Xunit;

namespace Fcg.Catalog.Tests.Unit.Interfaces;

public class IItemBibliotecaRepositoryTests
{
    [Fact]
    public void DeveExporApenasAVerificacaoDeExistenciaEAAdicao()
    {
        // Guarda estrutural: o repositório terminou como escrita pura mais a pergunta que a
        // invariante de compra faz à fonte da verdade. Uma listagem de volta aqui seria o caminho
        // de apresentação reentrando no domínio, e a leitura passaria a ter duas fontes.
        string[] operacoes =
        [
            .. typeof(IItemBibliotecaRepository).GetMethods().Select(metodo => metodo.Name),
        ];

        operacoes.Should().BeEquivalentTo(["ExisteAsync", "AdicionarAsync"]);
    }
}
