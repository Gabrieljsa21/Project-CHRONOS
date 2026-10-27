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

        private const int MaximoFalhas = 4;
        // Depois de cada recusa a conquista vai para o fim da fila por 30, 60 e 120 min: a Steam
        // recusa por algumas horas e volta a aceitar (The Darkness II em 2026-10-26)
        private const double AdiamentoAposFalhaMinutos = 30;
        private const double MaximoMinutosPorCiclo = 2.0;

        public static int Executar(uint appid, int pidPai, Config cfg)
        {
            Log.Prefixo = $"sessao {appid}";
            var estado = EstadoJogo.Carregar(appid);
            if (estado == null && !cfg.ManterAberto)
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

                // Recusadas pela Steam ganham uma nova rodada a cada sessão aberta
                if (estado != null && (!estado.Concluido || estado.TemRecusadas))
                {
                    var codigo = LiberarConquistas(cliente, appid, pidPai, estado, cfg);
                    if (codigo != Ok || !estado.Concluido) return codigo;
                }
                if (!cfg.ManterAberto) return Ok;

                // Só conta horas: a Steam mostra o jogo aberto enquanto o processo estiver conectado
                Log.Escrever($"aberto só para contar horas ({(estado == null ? "sem plano de conquistas" : "conquistas concluídas")})");
                while (Aguardar(cliente, pidPai, TimeSpan.FromSeconds(60)))
                {
                    // Plano criado depois (o jogo ganhou conquistas): o agendador reabre no modo normal
                    var atual = EstadoJogo.Carregar(appid);
                    if (atual != null && !atual.Concluido)
                    {
                        Log.Escrever("plano de conquistas disponível; reabrindo a sessão");
                        return Ok;
                    }
                }
                Log.Escrever("agendador encerrado; fechando sessão");
                return Ok;
            }
        }

        // Laço normal: desbloqueia as conquistas no minuto planejado até concluir o jogo ou o
        // agendador sumir (aí estado.Concluido continua falso)
        private static int LiberarConquistas(SAM.API.Client cliente, uint appid, int pidPai, EstadoJogo estado, Config cfg)
        {
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
                    Desbloquear(cliente, appid, estado, proxima, cfg.Simulacao);
                }
                estado.Salvar();
            }

            discord?.Dispose();
            Log.Escrever($"{estado.Nome} concluído");
            return Ok;
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
            var recusadas = estado.Conquistas.Where(c => c.Recusada).ToList();
            foreach (var c in estado.Conquistas.Where(c => c.Situacao == Situacao.Pendente || c.Situacao == Situacao.SemDlc || c.Recusada
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

            // As recusadas que voltam a pendentes saem espaçadas a partir de agora, nunca em bloco
            var minuto = estado.MinutosJogados;
            var sorteio = new Random();
            foreach (var c in recusadas.Where(c => c.Situacao == Situacao.Pendente))
            {
                c.Falhas = 0;
                minuto += 15 + sorteio.NextDouble() * 30;
                c.Minuto = Math.Round(minuto, 1);
            }
            var novaChance = recusadas.Count(c => c.Situacao == Situacao.Pendente);
            if (novaChance > 0) Log.Escrever($"{novaChance} conquista(s) recusada(s) pela Steam voltam a pendentes");
            var semDlc = estado.Conquistas.Count(c => c.Situacao == Situacao.SemDlc);
            if (semDlc > 0) Log.Escrever($"{semDlc} conquista(s) de DLC que a conta não tem ficam de fora");

            foreach (var c in estado.Conquistas.Where(c => c.Nome == c.ApiName))
            {
                var nome = stats.GetAchievementDisplayAttribute(c.ApiName, "name");
                if (!string.IsNullOrEmpty(nome)) c.Nome = nome;
            }
        }

        private static void Desbloquear(SAM.API.Client cliente, uint appid, EstadoJogo estado, Conquista c, bool simulacao)
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

            // A Steam recusa por um tempo (estatísticas da sessão invalidadas) ou para sempre
            // (conquista protegida pelo servidor do jogo); pedir as estatísticas de novo cobre o primeiro caso
            c.Falhas++;
            if (c.Falhas >= MaximoFalhas)
            {
                c.Situacao = Situacao.Recusada;
                Log.Escrever($"a Steam recusou '{c.Nome}' {c.Falhas} vezes; nova tentativa na próxima sessão");
                return;
            }
            var adiamento = AdiamentoAposFalhaMinutos * Math.Pow(2, c.Falhas - 1);
            c.Minuto = Math.Round(estado.MinutosJogados + adiamento, 1);
            Log.Escrever($"falha ao desbloquear '{c.Nome}' (tentativa {c.Falhas}/{MaximoFalhas}); nova tentativa em {adiamento:F0} min de jogo");
            if (!ReceberStats(cliente, appid)) Log.Escrever("a Steam não devolveu as estatísticas do usuário de novo");
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
