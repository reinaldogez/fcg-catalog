using Fcg.Catalog.Domain.Entities;

namespace Fcg.Catalog.Application.Abstractions;

// Versão externa de uma escrita no índice de busca: o instante da última alteração do jogo, em
// microssegundos desde a epoch Unix. Toda mutação do jogo avança esse instante, então a versão
// cresce com o estado, e o índice recusa a escrita de um estado mais velho que o gravado.
public static class VersaoDoJogo
{
    // Microssegundo porque é a resolução com que o PostgreSQL guarda o instante. A versão sempre
    // sai do valor relido do banco: o DateTime em memória do use case carrega os décimos de
    // microssegundo que o banco descarta, e daria uma versão que nenhuma releitura reproduz.
    public static long De(Jogo jogo)
    {
        // O banco devolve o instante em UTC; um valor local é normalizado para não deslocar a
        // versão pelo fuso da máquina.
        DateTime instante =
            jogo.AtualizadoEm.Kind == DateTimeKind.Local
                ? jogo.AtualizadoEm.ToUniversalTime()
                : jogo.AtualizadoEm;

        return (instante.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMicrosecond;
    }
}
