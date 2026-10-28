using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Chronos
{
    // Espaça os desbloqueios de todas as sessões: entre dois, de qualquer jogo, passam de 1 a 2 min.
    // Cada sessão só solta uma por ciclo, mas sessões que reabrem juntas com conquista vencida
    // soltavam todas no mesmo minuto (14 às 16:30 de 2026-10-28)
    internal static class Vez
    {
        private const double IntervaloMinimoSegundos = 60;
        private const double IntervaloMaximoSegundos = 120;
        private static readonly Random Sorteio = new Random();

        private static string Caminho => Path.Combine(Caminhos.Dados, "proximo_desbloqueio.txt");

        // Verdadeiro se esta sessão pode desbloquear agora; já reserva o intervalo até a próxima
        public static bool Pegar()
        {
            using (var mutex = new Mutex(false, @"Local\Project-CHRONOS-desbloqueio"))
            {
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) return false;
                }
                catch (AbandonedMutexException)
                {
                    // A sessão dona caiu segurando o mutex; ele passa para esta
                }
                try
                {
                    var agora = DateTime.Now;
                    if (File.Exists(Caminho)
                        && DateTime.TryParse(File.ReadAllText(Caminho), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var liberadaEm)
                        && agora < liberadaEm)
                    {
                        return false;
                    }
                    var espera = IntervaloMinimoSegundos + Sorteio.NextDouble() * (IntervaloMaximoSegundos - IntervaloMinimoSegundos);
                    File.WriteAllText(Caminho, agora.AddSeconds(espera).ToString("o", CultureInfo.InvariantCulture));
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }
    }
}
