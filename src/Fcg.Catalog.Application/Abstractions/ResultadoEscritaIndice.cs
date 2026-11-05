namespace Fcg.Catalog.Application.Abstractions;

// Desfechos esperados de uma escrita no índice de busca. Nenhum deles é erro: qualquer outra
// resposta do índice vira exceção, para que quem chamou possa repetir.
public enum ResultadoEscritaIndice
{
    // A escrita foi aplicada, ou não havia o que remover.
    Aplicada,

    // O índice já guarda uma versão mais nova do jogo; a escrita foi descartada.
    Obsoleta,

    // O índice ainda não existe. Quem o cria é a reconstrução, que lê a fonte depois de criá-lo.
    IndiceAusente,
}
