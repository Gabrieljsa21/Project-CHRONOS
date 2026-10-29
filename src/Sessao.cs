using System;
using System.Collections.Generic;
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
        public const int SteamReiniciada = 6;

        // As conquistas saem sempre na ordem do cronograma: a que a Steam recusa segura a fila e
        // é tentada de novo a cada 15 min. Depois de 1 h recusando, o jogo para até o usuário
        // retomar (--retomar), e o indicador da GAIA mostra "!"
        private static readonly TimeSpan EsperaAposFalha = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan TempoMaximoRecusando = TimeSpan.FromHours(1);
        private const double MaximoMinutosPorCiclo = 2.0;
        // Jogo concluído aberto só para contar horas confere com a Steam se alguma conquista
        // foi removida da conta ou se o jogo ganhou conquistas novas
        private static readonly TimeSpan IntervaloConferencia = TimeSpan.FromMinutes(30);

        // Instância da Steam a que a sessão se conectou; se ela reiniciar, a conexão fica morta
        private static int pidSteam;

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
                pidSteam = SteamLocal.Pid();

                // Recusadas da 0.5.1 e anteriores voltam a pendentes na sincronização
                if (estado != null && !estado.Parado && (!estado.Concluido || estado.TemRecusadas))
                {
                    var codigo = LiberarConquistas(cliente, appid, pidPai, estado, cfg);
                    if (codigo != Ok || !estado.Concluido) return codigo;
                }
                if (!cfg.ManterAberto) return Ok;

                // Só conta horas: a Steam mostra o jogo aberto enquanto o processo estiver conectado
                Log.Escrever($"aberto só para contar horas ({(estado == null ? "sem plano de conquistas" : "conquistas concluídas")})");
                var proximaConferencia = DateTime.MinValue;
                while (Aguardar(cliente, pidPai, TimeSpan.FromSeconds(60)))
                {
                    if (SteamLocal.Mudou(pidSteam))
                    {
                        Log.Escrever("a Steam reiniciou; fechando a sessão para o agendador reabrir");
                        return SteamReiniciada;
                    }
                    // Conquista removida da conta ou nova no jogo reabre o plano de um concluído
                    if (estado != null && DateTime.Now >= proximaConferencia)
                    {
                        proximaConferencia = DateTime.Now + IntervaloConferencia;
                        var conferido = EstadoJogo.Carregar(appid);
                        if (conferido != null && ReceberStats(cliente, appid))
                        {
                            if (Conferir(cliente, conferido, cfg.Simulacao))
                            {
                                Replanejar(conferido, cfg, conferido.DuracaoTotalMinutos, true);
                                conferido.Salvar();
                                Log.Escrever("conquistas voltaram a pendentes; reabrindo a sessão");
                                return Ok;
                            }
                            conferido.Salvar();
                        }
                    }
                    // Plano criado depois (o jogo ganhou conquistas): o agendador reabre no modo normal
                    var atual = EstadoJogo.Carregar(appid);
                    if (atual != null && !atual.Concluido && !atual.Parado)
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

            var voltaram = Conferir(cliente, estado, cfg.Simulacao);
            // Refaz na primeira sessão, nos planos sem as horas da Steam (0.7.0 e anteriores),
            // quando a duracao_horas do config.json muda e quando conquistas voltaram a pendentes
            var jogo = cfg.Jogos.FirstOrDefault(j => j.Appid == appid);
            var total = Planejador.DuracaoAtual(estado, jogo, cfg);
            if (!estado.Sincronizado || estado.MinutosSteam == null || Math.Abs(total - estado.DuracaoTotalMinutos) >= 1 || voltaram)
            {
                Replanejar(estado, cfg, total, voltaram);
            }
            estado.Salvar();
            Log.Escrever($"sessão iniciada: {estado.Nome}, {estado.MinutosJogados:F0}/{estado.DuracaoMinutos:F0} min, {estado.Pendentes.Count()} pendentes{(cfg.Simulacao ? " (simulação)" : "")}");

            var discord = cfg.Discord ? Discord.Criar(appid) : null;
            if (cfg.Discord && discord == null) Log.Escrever("jogo fora da lista do Discord; sem atividade lá");
            discord?.Manter();

            var acelerar = double.TryParse(Environment.GetEnvironmentVariable("CHRONOS_ACELERAR"), out var fator) ? fator : 1.0;
            var relogio = Stopwatch.StartNew();
            DateTime? primeiraRecusa = null;
            var proximaTentativa = DateTime.MinValue;
            while (!estado.Concluido && !estado.Parado)
            {
                if (!Aguardar(cliente, pidPai, TimeSpan.FromSeconds(60)))
                {
                    Log.Escrever("agendador encerrado; fechando sessão");
                    discord?.Dispose();
                    return Ok;
                }
                if (SteamLocal.Mudou(pidSteam))
                {
                    Log.Escrever("a Steam reiniciou; fechando a sessão para o agendador reabrir");
                    discord?.Dispose();
                    estado.Salvar();
                    return SteamReiniciada;
                }
                discord?.Manter();

                // Limite por ciclo evita contar como jogado o tempo em que o PC hibernou
                var delta = Math.Min(relogio.Elapsed.TotalMinutes, MaximoMinutosPorCiclo);
                relogio.Restart();
                estado.MinutosJogados += delta * acelerar;

                // No máximo uma por ciclo, e de 1 a 2 min depois da última de qualquer jogo (Vez),
                // para nunca sair um bloco de conquistas no mesmo instante
                var proxima = estado.Pendentes.FirstOrDefault();
                if (proxima != null && proxima.Minuto <= estado.MinutosJogados && DateTime.Now >= proximaTentativa && Vez.Pegar())
                {
                    if (Desbloquear(cliente, estado, proxima, cfg.Simulacao))
                    {
                        primeiraRecusa = null;
                    }
                    else
                    {
                        primeiraRecusa = primeiraRecusa ?? DateTime.Now;
                        if (DateTime.Now - primeiraRecusa >= TempoMaximoRecusando)
                        {
                            estado.ParadoEm = DateTime.Now;
                            estado.ParadoNa = proxima.ApiName;
                            Log.Escrever($"a Steam recusa '{proxima.Nome}' há {TempoMaximoRecusando.TotalMinutes:F0} min ({proxima.Falhas} tentativas); jogo parado até retomar");
                        }
                        else
                        {
                            proximaTentativa = DateTime.Now + EsperaAposFalha;
                            Log.Escrever($"falha ao desbloquear '{proxima.Nome}' (tentativa {proxima.Falhas}); nova tentativa em {EsperaAposFalha.TotalMinutes:F0} min");
                            if (!ReceberStats(cliente, appid)) Log.Escrever("a Steam não devolveu as estatísticas do usuário de novo");
                        }
                    }
                }
                estado.Salvar();
            }

            discord?.Dispose();
            if (!estado.Parado) Log.Escrever($"{estado.Nome} concluído");
            return Ok;
        }

        private static void Replanejar(EstadoJogo estado, Config cfg, double total, bool fatiaMinima)
        {
            // Sem o localconfig.vdf fica 0, para não refazer o cronograma em toda sessão
            estado.MinutosSteam = TempoSteam.Minutos(estado.Appid) ?? 0;
            estado.MinutosJogados = Math.Max(estado.MinutosJogados, estado.MinutosSteam.Value);
            estado.DuracaoTotalMinutos = total;
            Planejador.Replanejar(estado, cfg, new Random(), fatiaMinima);
            estado.Sincronizado = true;
            Log.Escrever($"cronograma refeito com as {estado.Pendentes.Count()} pendentes: {estado.MinutosJogados / 60:F1} h jogadas (Steam: {estado.MinutosSteam / 60:F1} h), termina em {estado.DuracaoMinutos / 60:F1} h de {total / 60:F1} h do jogo");
        }

        // Reconfere o plano com a Steam e sincroniza. Verdadeiro se há mais pendentes que antes
        // (conquista removida da conta, nova no jogo ou de DLC que chegou)
        private static bool Conferir(SAM.API.Client cliente, EstadoJogo estado, bool simulacao)
        {
            var antes = estado.Pendentes.Count();
            Reabrir(cliente, estado);
            Sincronizar(cliente, estado, simulacao);
            return estado.Pendentes.Count() > antes;
        }

        // Conquista que a conta tinha e perdeu (o usuário a removeu, ex.: pelo SAM) volta a
        // pendente, e conquista que o jogo ganhou depois do plano entra como pendente; a
        // Sincronizar marca a que o usuário já tem e a de DLC que falta. As desbloqueadas só em
        // simulação ficam de fora: na Steam elas nunca existiram.
        private static void Reabrir(SAM.API.Client cliente, EstadoJogo estado)
        {
            var stats = cliente.SteamUserStats;
            var removidas = new List<string>();
            foreach (var c in estado.Conquistas.Where(c => (c.Situacao == Situacao.Desbloqueada || c.Situacao == Situacao.JaTinha) && !c.Simulada))
            {
                if (!stats.GetAchievement(c.ApiName, out var tem) || tem) continue;
                c.Situacao = Situacao.Pendente;
                c.DesbloqueadaEm = null;
                c.Falhas = 0;
                removidas.Add(c.Nome);
            }
            if (removidas.Count > 0) Log.Escrever($"{removidas.Count} conquista(s) removida(s) da conta voltam a pendentes: {string.Join(", ", removidas)}");

            try
            {
                var novas = Planejador.ConquistasNovas(estado, EsquemaSteam.Conquistas(estado.Appid));
                foreach (var c in novas)
                {
                    var nome = stats.GetAchievementDisplayAttribute(c.ApiName, "name");
                    if (!string.IsNullOrEmpty(nome)) c.Nome = nome;
                    estado.Conquistas.Add(c);
                }
                if (novas.Count > 0) Log.Escrever($"{novas.Count} conquista(s) nova(s) no jogo entram no plano: {string.Join(", ", novas.Select(c => c.Nome))}");
            }
            catch (Exception e)
            {
                Log.Escrever($"não deu para conferir conquistas novas ({e.Message}); nova tentativa na próxima conferência");
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

        // Verdadeiro se a Steam gravou. Com atraso (a fila ficou presa numa recusa), as seguintes
        // andam o mesmo tanto, para não saírem em bloco
        private static bool Desbloquear(SAM.API.Client cliente, EstadoJogo estado, Conquista c, bool simulacao)
        {
            if (simulacao)
            {
                c.Situacao = Situacao.Desbloqueada;
                c.Simulada = true;
                c.DesbloqueadaEm = DateTime.Now;
                Log.Escrever($"[simulação] desbloquearia '{c.Nome}' ({c.Porcentagem:F1}%) no minuto {c.Minuto:F0}");
                return true;
            }

            // A Steam recusa por um tempo (estatísticas da sessão invalidadas) ou para sempre
            // (conquista protegida pelo servidor do jogo)
            if (!cliente.SteamUserStats.SetAchievement(c.ApiName, true) || !cliente.SteamUserStats.StoreStats())
            {
                c.Falhas++;
                return false;
            }

            c.Situacao = Situacao.Desbloqueada;
            c.DesbloqueadaEm = DateTime.Now;
            c.Falhas = 0;
            Log.Escrever($"desbloqueada '{c.Nome}' ({c.Porcentagem:F1}%) no minuto {c.Minuto:F0}");
            var atraso = estado.MinutosJogados - c.Minuto;
            if (atraso > 1)
            {
                foreach (var seguinte in estado.Pendentes) seguinte.Minuto = Math.Round(seguinte.Minuto + atraso, 1);
            }
            return true;
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
