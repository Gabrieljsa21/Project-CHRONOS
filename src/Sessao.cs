using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Chronos
{
    // Processo filho que se apresenta à Steam como o jogo (a Steam mostra "jogando" e conta horas)
    // e desbloqueia as conquistas cujo minuto planejado já foi alcançado.
    // Um processo por jogo, porque a SAM.API só se conecta a um appid por processo.
    internal static class Sessao
    {
        public const int Ok = 0;
        public const int SemEstado = 2;
        public const int FalhaSteam = 3;
        public const int FalhaStats = 4;

        private const int MaximoFalhas = 3;
        private const double MaximoMinutosPorCiclo = 2.0;

        public static int Executar(uint appid, int pidPai, Config cfg)
        {
            Log.Prefixo = $"sessao {appid}";
            var estado = EstadoJogo.Carregar(appid);
            if (estado == null)
            {
                Log.Escrever("sem estado planejado; encerrando");
                return SemEstado;
            }

            using (var cliente = new SAM.API.Client())
            {
                try
                {
                    cliente.Initialize(appid);
                }
                catch (SAM.API.ClientInitializeException e)
                {
                    Log.Escrever($"falha ao conectar na Steam ({e.Failure}): {e.Message}");
                    return FalhaSteam;
                }

                if (!ReceberStats(cliente, appid))
                {
                    Log.Escrever("a Steam não devolveu as estatísticas do usuário");
                    return FalhaStats;
                }

                Sincronizar(cliente, estado, cfg.Simulacao);
                if (!estado.Sincronizado)
                {
                    Planejador.Replanejar(estado, cfg, new Random());
                    estado.Sincronizado = true;
                    Log.Escrever($"cronograma refeito com as {estado.Pendentes.Count()} pendentes: {estado.DuracaoMinutos / 60:F1} h no total");
                }
                estado.Salvar();
                Log.Escrever($"sessão iniciada: {estado.Nome}, {estado.MinutosJogados:F0}/{estado.DuracaoMinutos:F0} min, {estado.Pendentes.Count()} pendentes{(cfg.Simulacao ? " (simulação)" : "")}");

                var discord = cfg.Discord ? Discord.Criar(appid) : null;
                if (cfg.Discord && discord == null) Log.Escrever("jogo fora da lista do Discord; sem atividade lá");
                discord?.Manter();

                var acelerar = double.TryParse(Environment.GetEnvironmentVariable("CHRONOS_ACELERAR"), out var fator) ? fator : 1.0;
                var relogio = Stopwatch.StartNew();
                while (!estado.Concluido)
                {
                    if (!Aguardar(cliente, pidPai, TimeSpan.FromSeconds(60)))
                    {
                        Log.Escrever("agendador encerrado; fechando sessão");
                        discord?.Dispose();
                        return Ok;
                    }
                    discord?.Manter();

                    // Limite por ciclo evita contar como jogado o tempo em que o PC hibernou
                    var delta = Math.Min(relogio.Elapsed.TotalMinutes, MaximoMinutosPorCiclo);
                    relogio.Restart();
                    estado.MinutosJogados += delta * acelerar;

                    // No máximo uma por ciclo, para nunca sair um bloco de conquistas no mesmo instante
                    var proxima = estado.Pendentes.FirstOrDefault();
                    if (proxima != null && proxima.Minuto <= estado.MinutosJogados)
                    {
                        Desbloquear(cliente, proxima, cfg.Simulacao);
                    }
                    estado.Salvar();
                }

                discord?.Dispose();
                Log.Escrever($"{estado.Nome} concluído");
                return Ok;
            }
        }

        private static bool ReceberStats(SAM.API.Client cliente, uint appid)
        {
            var recebido = false;
            var resultado = 0;
            var callback = cliente.CreateAndRegisterCallback<SAM.API.Callbacks.UserStatsReceived>();
            callback.OnRun += parametro =>
            {
                if (parametro.GameId != appid) return;
                recebido = true;
                resultado = parametro.Result;
            };

            cliente.SteamUserStats.RequestUserStats(cliente.SteamUser.GetSteamId());
            var limite = Stopwatch.StartNew();
            while (!recebido && limite.Elapsed < TimeSpan.FromSeconds(60))
            {
                cliente.RunCallbacks(false);
                Thread.Sleep(100);
            }
            // 1 = k_EResultOK
            return recebido && resultado == 1;
        }

        // Marca o que o usuário já tinha, descarta nomes que não existem no schema do jogo e
        // separa as conquistas de DLC que a conta não tem (voltam a pendentes se a DLC chegar).
        // Fora da simulação, as desbloqueadas só em simulação voltam a pendentes.
        private static void Sincronizar(SAM.API.Client cliente, EstadoJogo estado, bool simulacao)
        {
            var stats = cliente.SteamUserStats;
            var dlcs = EsquemaSteam.ConquistasDeDlc(estado.Appid);
            foreach (var c in estado.Conquistas.Where(c => c.Situacao == Situacao.Pendente || c.Situacao == Situacao.SemDlc
                                                           || (!simulacao && c.Simulada && c.Situacao == Situacao.Desbloqueada)))
            {
                c.Simulada = false;
                c.DesbloqueadaEm = null;
                if (!stats.GetAchievement(c.ApiName, out var tem))
                {
                    c.Situacao = Situacao.Ignorada;
                    Log.Escrever($"'{c.ApiName}' não existe no schema; ignorada");
                    continue;
                }
                if (tem)
                {
                    c.Situacao = Situacao.JaTinha;
                    continue;
                }
                var temDlc = !dlcs.TryGetValue(c.ApiName, out var dlc) || cliente.SteamApps008.IsSubscribedApp(dlc);
                c.Situacao = temDlc ? Situacao.Pendente : Situacao.SemDlc;
            }
            var semDlc = estado.Conquistas.Count(c => c.Situacao == Situacao.SemDlc);
            if (semDlc > 0) Log.Escrever($"{semDlc} conquista(s) de DLC que a conta não tem ficam de fora");

            foreach (var c in estado.Conquistas.Where(c => c.Nome == c.ApiName))
            {
                var nome = stats.GetAchievementDisplayAttribute(c.ApiName, "name");
                if (!string.IsNullOrEmpty(nome)) c.Nome = nome;
            }
        }

        private static void Desbloquear(SAM.API.Client cliente, Conquista c, bool simulacao)
        {
            if (simulacao)
            {
                c.Situacao = Situacao.Desbloqueada;
                c.Simulada = true;
                c.DesbloqueadaEm = DateTime.Now;
                Log.Escrever($"[simulação] desbloquearia '{c.Nome}' ({c.Porcentagem:F1}%) no minuto {c.Minuto:F0}");
                return;
            }

            if (cliente.SteamUserStats.SetAchievement(c.ApiName, true) && cliente.SteamUserStats.StoreStats())
            {
                c.Situacao = Situacao.Desbloqueada;
                c.DesbloqueadaEm = DateTime.Now;
                Log.Escrever($"desbloqueada '{c.Nome}' ({c.Porcentagem:F1}%) no minuto {c.Minuto:F0}");
                return;
            }

            // Conquistas protegidas pelo servidor do jogo recusam a gravação
            c.Falhas++;
            Log.Escrever($"falha ao desbloquear '{c.Nome}' (tentativa {c.Falhas}/{MaximoFalhas})");
            if (c.Falhas >= MaximoFalhas) c.Situacao = Situacao.Ignorada;
        }

        // Espera processando callbacks; falso se o agendador que abriu a sessão sumiu
        private static bool Aguardar(SAM.API.Client cliente, int pidPai, TimeSpan duracao)
        {
            var limite = Stopwatch.StartNew();
            while (limite.Elapsed < duracao)
            {
                cliente.RunCallbacks(false);
                Thread.Sleep(1000);
            }
            return PaiVivo(pidPai);
        }

        private static bool PaiVivo(int pid)
        {
            if (pid <= 0) return true;
            try
            {
                return !Process.GetProcessById(pid).HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
