using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Chronos
{
    // Processo principal: decide quais jogos ficam "abertos" agora e mantém uma sessão por jogo
    internal static class Agendador
    {
        private static readonly TimeSpan Ciclo = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan EsperaAposFalhaSessao = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EsperaAposFalhaPlano = TimeSpan.FromMinutes(30);
        // Sessão que roda esse tempo sem cair zera a contagem de quedas seguidas
        private static readonly TimeSpan SessaoEstavel = TimeSpan.FromMinutes(10);
        // Uma queda isolada (Steam reiniciou) se resolve sozinha; a partir da 2ª seguida vira aviso
        private const int QuedasParaAvisar = 2;

        // "Finalizar tarefa" do Gerenciador de Tarefas encerra o processo com código 1
        private const int CodigoEncerradaPorFora = 1;

        private static readonly Dictionary<uint, Process> Sessoes = new Dictionary<uint, Process>();
        private static readonly Dictionary<uint, string> Nomes = new Dictionary<uint, string>();
        // Steam aberta quando cada sessão começou: separa o "Finalizar tarefa" da Steam reiniciada
        private static readonly Dictionary<uint, int> SteamDaSessao = new Dictionary<uint, int>();
        // Jogos cuja sessão foi encerrada por fora: ficam parados até o agendador reiniciar
        private static readonly HashSet<uint> Pausados = new HashSet<uint>();
        private static readonly Dictionary<uint, DateTime> BloqueadoAte = new Dictionary<uint, DateTime>();
        // Separado do bloqueio de sessão: jogo sem plano ainda pode ficar aberto com manter_aberto
        private static readonly Dictionary<uint, DateTime> PlanoBloqueadoAte = new Dictionary<uint, DateTime>();
        // Para o saude.json: quedas seguidas de cada sessão, com o motivo da última, e a falha do plano
        private static readonly Dictionary<uint, DateTime> AbertaEm = new Dictionary<uint, DateTime>();
        private static readonly Dictionary<uint, int> QuedasSeguidas = new Dictionary<uint, int>();
        private static readonly Dictionary<uint, string> MotivoQueda = new Dictionary<uint, string>();
        private static readonly Dictionary<uint, string> MotivoPlano = new Dictionary<uint, string>();
        private static readonly string[] CodigosDias = { "dom", "seg", "ter", "qua", "qui", "sex", "sab" };

        public static int Executar()
        {
            using (var mutex = new Mutex(true, @"Local\Project-CHRONOS", out var unico))
            {
                if (!unico)
                {
                    Log.Escrever("já existe um agendador rodando; encerrando");
                    return 1;
                }

                Log.Escrever("agendador iniciado");
                var rng = new Random();
                while (true)
                {
                    try
                    {
                        var cfg = Config.Carregar();
                        Ciclar(cfg, rng);
                        Saude.Gravar(Problemas(cfg));
                    }
                    catch (Exception e)
                    {
                        Log.Escrever($"erro no ciclo: {e}");
                        Saude.Gravar(new[] { $"Erro no agendador: {e.Message}" });
                    }
                    Thread.Sleep(Ciclo);
                }
            }
        }

        private static void Ciclar(Config cfg, Random rng)
        {
            RecolherSessoesEncerradas();

            var pendentes = new List<uint>();
            var ociosos = new List<uint>();
            var estados = new Dictionary<uint, EstadoJogo>();
            foreach (var jogo in cfg.Jogos)
            {
                var estado = EstadoJogo.Carregar(jogo.Appid) ?? CriarPlano(jogo, cfg, rng);
                estados[jogo.Appid] = estado;
                Nomes[jogo.Appid] = estado?.Nome ?? jogo.Nome ?? jogo.Appid.ToString();
                if (estado != null && !estado.Concluido && !estado.Parado) pendentes.Add(jogo.Appid);
                else if (cfg.ManterAberto) ociosos.Add(jogo.Appid);
            }
            ReordenarFila(cfg, estados);

            // Os jogos com conquistas a liberar têm prioridade nas vagas de jogos_simultaneos
            var desejados = DentroDaJanela(cfg, DateTime.Now)
                ? pendentes.Concat(ociosos).Where(Liberado).Take(cfg.JogosSimultaneos > 0 ? cfg.JogosSimultaneos : int.MaxValue).ToList()
                : new List<uint>();

            foreach (var appid in Sessoes.Keys.Except(desejados).ToList())
            {
                Log.Escrever($"fechando sessão de {appid} (fora da janela ou fora da lista)");
                try { Sessoes[appid].Kill(); } catch (InvalidOperationException) { }
                Sessoes.Remove(appid);
                SteamDaSessao.Remove(appid);
                AbertaEm.Remove(appid);
            }

            foreach (var appid in desejados.Where(a => !Sessoes.ContainsKey(a)))
            {
                AbrirSessao(appid);
            }

            foreach (var appid in AbertaEm.Where(p => DateTime.Now - p.Value >= SessaoEstavel).Select(p => p.Key).ToList())
            {
                QuedasSeguidas.Remove(appid);
            }
        }

        private static List<string> Problemas(Config cfg)
        {
            var problemas = new List<string>();
            var naFila = new HashSet<uint>(cfg.Jogos.Select(j => j.Appid));
            string Nome(uint appid) => Nomes.TryGetValue(appid, out var nome) ? nome : appid.ToString();

            // Steam fechada derruba todas as sessões; o aviso dela basta
            var steamAberta = SteamLocal.Pid() != 0;
            if (!steamAberta) problemas.Add("Steam fechada: nenhum jogo consegue abrir");
            foreach (var appid in Pausados.Where(naFila.Contains))
            {
                problemas.Add($"{Nome(appid)}: encerrado por fora (Gerenciador de Tarefas); volta quando o CHRONOS reiniciar");
            }
            if (steamAberta)
            {
                foreach (var par in QuedasSeguidas.Where(p => p.Value >= QuedasParaAvisar && naFila.Contains(p.Key)))
                {
                    problemas.Add($"{Nome(par.Key)}: a sessão caiu {par.Value} vezes seguidas ({MotivoQueda[par.Key]})");
                }
            }
            foreach (var par in PlanoBloqueadoAte.Where(p => DateTime.Now < p.Value && naFila.Contains(p.Key)))
            {
                problemas.Add($"{Nome(par.Key)}: não foi possível montar o plano de conquistas ({MotivoPlano[par.Key]})");
            }
            return problemas;
        }

        private static string Motivo(int codigo)
        {
            switch (codigo)
            {
                case CodigoEncerradaPorFora:
                case Sessao.SteamReiniciada: return "a Steam reiniciou ou fechou";
                case Sessao.SemEstado: return "sem plano de conquistas";
                case Sessao.FalhaSteam: return "não conectou na Steam";
                case Sessao.FalhaStats: return "a Steam não devolveu as estatísticas";
                default: return $"código {codigo}";
            }
        }

        // Jogo concluído sobe para o topo da lista, o último concluído primeiro; os outros
        // mantêm a ordem do usuário, que continua decidindo quem pega as vagas
        private static void ReordenarFila(Config cfg, Dictionary<uint, EstadoJogo> estados)
        {
            bool Concluido(JogoConfig j) => estados.TryGetValue(j.Appid, out var e) && e != null && e.Concluido;
            var ordem = cfg.Jogos.Where(Concluido).OrderByDescending(j => estados[j.Appid].ConcluidoEm ?? DateTime.MinValue)
                .Concat(cfg.Jogos.Where(j => !Concluido(j))).Select(j => j.Appid).ToList();
            if (ordem.SequenceEqual(cfg.Jogos.Select(j => j.Appid))) return;

            try
            {
                Config.SalvarOrdemJogos(ordem);
                Log.Escrever("fila reordenada: jogos concluídos no topo");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // A GAIA pode estar gravando o config.json agora: tenta de novo no próximo ciclo
                Log.Escrever($"não foi possível reordenar a fila: {e.Message}");
            }
        }

        private static EstadoJogo CriarPlano(JogoConfig jogo, Config cfg, Random rng)
        {
            if (PlanoBloqueadoAte.TryGetValue(jogo.Appid, out var ate) && DateTime.Now < ate) return null;
            try
            {
                var estado = Planejador.Planejar(jogo, cfg, rng);
                estado.Salvar();
                Log.Escrever($"plano criado para {estado.Nome} ({jogo.Appid}): {estado.Conquistas.Count} conquistas em {estado.DuracaoMinutos / 60:F1} h, fonte {estado.Fonte}");
                return estado;
            }
            catch (Exception e)
            {
                Log.Escrever($"não foi possível planejar {jogo.Appid}: {e.Message}");
                PlanoBloqueadoAte[jogo.Appid] = DateTime.Now + EsperaAposFalhaPlano;
                MotivoPlano[jogo.Appid] = e.Message;
                return null;
            }
        }

        private static void AbrirSessao(uint appid)
        {
            var info = new ProcessStartInfo
            {
                FileName = ProcessoSessao.Preparar(Process.GetCurrentProcess().MainModule.FileName, appid, Nomes[appid]),
                Arguments = $"--sessao {appid} --pai {Process.GetCurrentProcess().Id}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Sessoes[appid] = Process.Start(info);
            SteamDaSessao[appid] = SteamLocal.Pid();
            AbertaEm[appid] = DateTime.Now;
            Log.Escrever($"sessão aberta para {appid}");
        }

        private static void RecolherSessoesEncerradas()
        {
            foreach (var par in Sessoes.Where(p => p.Value.HasExited).ToList())
            {
                var codigo = par.Value.ExitCode;
                Sessoes.Remove(par.Key);
                SteamDaSessao.TryGetValue(par.Key, out var steamAntes);
                SteamDaSessao.Remove(par.Key);
                AbertaEm.Remove(par.Key);
                // A sessão presa a uma Steam que reiniciou cai com o mesmo código 1
                var steamReiniciou = codigo == CodigoEncerradaPorFora && (steamAntes == 0 || SteamLocal.Mudou(steamAntes));
                if (codigo == Sessao.Ok)
                {
                    QuedasSeguidas.Remove(par.Key);
                }
                else if (codigo != CodigoEncerradaPorFora || steamReiniciou)
                {
                    QuedasSeguidas[par.Key] = (QuedasSeguidas.TryGetValue(par.Key, out var quedas) ? quedas : 0) + 1;
                    MotivoQueda[par.Key] = Motivo(codigo);
                }
                if (steamReiniciou || codigo == Sessao.SteamReiniciada)
                {
                    Log.Escrever($"sessão de {par.Key} caiu porque a Steam reiniciou ou fechou (código {codigo}); nova tentativa em {EsperaAposFalhaSessao.TotalMinutes} min");
                    BloqueadoAte[par.Key] = DateTime.Now + EsperaAposFalhaSessao;
                }
                else if (codigo == CodigoEncerradaPorFora)
                {
                    Log.Escrever($"sessão de {par.Key} encerrada por fora (Gerenciador de Tarefas); parado até o CHRONOS reiniciar");
                    Pausados.Add(par.Key);
                }
                else if (codigo != Sessao.Ok)
                {
                    // Steam fechada, jogo não possuído etc.: tenta de novo mais tarde
                    Log.Escrever($"sessão de {par.Key} terminou com código {codigo}; nova tentativa em {EsperaAposFalhaSessao.TotalMinutes} min");
                    BloqueadoAte[par.Key] = DateTime.Now + EsperaAposFalhaSessao;
                }
            }
        }

        private static bool Liberado(uint appid) =>
            !Pausados.Contains(appid) && (!BloqueadoAte.TryGetValue(appid, out var ate) || DateTime.Now >= ate);

        internal static bool DentroDaJanela(Config cfg, DateTime agora)
        {
            if (cfg.Janelas == null || cfg.Janelas.Count == 0) return true;

            foreach (var janela in cfg.Janelas)
            {
                var inicio = Hora(janela.Inicio);
                var fim = Hora(janela.Fim);
                // Confere a janela que começou hoje e a de ontem, que pode atravessar a meia-noite
                for (var dias = 0; dias >= -1; dias--)
                {
                    var dia = agora.Date.AddDays(dias);
                    if (janela.Dias.Count > 0 && !janela.Dias.Contains(CodigosDias[(int)dia.DayOfWeek])) continue;
                    var de = dia + inicio;
                    var ate = dia + fim;
                    if (fim <= inicio) ate = ate.AddDays(1);
                    if (agora >= de && agora < ate) return true;
                }
            }
            return false;
        }

        private static TimeSpan Hora(string texto) =>
            texto == "24:00" ? TimeSpan.FromDays(1) : TimeSpan.ParseExact(texto, @"hh\:mm", CultureInfo.InvariantCulture);
    }
}
