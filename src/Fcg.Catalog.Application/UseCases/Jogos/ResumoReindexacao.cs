namespace Fcg.Catalog.Application.UseCases.Jogos;

// Contagem final da reconstrução do índice de busca. Pulou indica que o índice já tinha documentos
// e a reconstrução condicional não reescreveu nada.
public sealed record ResumoReindexacao(int Indexados, int Removidos, bool Pulou);
