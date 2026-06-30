namespace Fcg.Catalog.Domain.Enums;

// Persistido como int ordinal. Os valores são EXPLÍCITOS e append-only de propósito:
// sob armazenamento ordinal o banco grava o inteiro da posição, então reordenar a
// declaração ou inserir um membro no meio sem valor fixo mudaria silenciosamente o
// significado das linhas históricas (um 1 gravado como Aprovado passaria a ser lido como
// outro membro) — corrupção sem erro. Fixar = 0/1/2 desacopla o número da posição.
// NUNCA reordenar nem reusar um valor já atribuído; novos membros sempre com valor novo.
public enum StatusPedido
{
    Pendente = 0,
    Aprovado = 1,
    Rejeitado = 2,
}
