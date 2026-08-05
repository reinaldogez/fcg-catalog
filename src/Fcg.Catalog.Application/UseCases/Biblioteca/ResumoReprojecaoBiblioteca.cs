namespace Fcg.Catalog.Application.UseCases.Biblioteca;

// Contagem final da reconstrução. O item pulado é parte do resultado, não ruído de log: quem executa
// precisa saber que houve linha sem contraparte sem ter de reler o fluxo inteiro.
public sealed record ResumoReprojecaoBiblioteca(int Projetados, int Pulados);
