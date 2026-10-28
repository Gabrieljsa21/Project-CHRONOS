using System;
using System.Diagnostics;
using System.Linq;

namespace Chronos
{
    // Identifica a instância do cliente da Steam aberta agora. Quando a Steam reinicia, as sessões
    // continuam ligadas à instância antiga e caem com o mesmo código 1 do "Finalizar tarefa"
    internal static class SteamLocal
    {
        // 0 quando a Steam não está aberta
        public static int Pid()
        {
            var processos = Process.GetProcessesByName("steam");
            try
            {
                return processos.Select(p => p.Id).DefaultIfEmpty(0).Min();
            }
            finally
            {
                foreach (var p in processos) p.Dispose();
            }
        }

        public static bool Mudou(int pidAnterior) => pidAnterior != 0 && Pid() != pidAnterior;
    }
}
