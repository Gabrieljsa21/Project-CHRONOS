using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

        // "Finalizar tarefa" do Gerenciador de Tarefas encerra o processo com código 1
        private const int CodigoEncerradaPorFora = 1;

        private static readonly Dictionary<uint, Process> Sessoes = new Dictionary<uint, Process>();
        private static readonly Dictionary<uint, string> Nomes = new Dictionary<uint, string>();
        // Jogos cuja sessão foi encerrada por fora: ficam parados até o agendador reiniciar
        private static readonly HashSet<uint> Pausados = new HashSet<uint>();
        private static readonly Dictionary<uint, DateTime> BloqueadoAte = new Dictionary<uint, DateTime>();
        // Separado do bloqueio de sessão: jogo sem plano ainda pode ficar aberto com manter_aberto
        private static readonly Dictionary<uint, DateTime> PlanoBloqueadoAte = new Dictionary<uint, DateTime>();
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
                        Ciclar(Config.Carregar(), rng);
                    }
                    catch (Exception e)
                    {
                        Log.Escrever($"erro no ciclo: {e}");
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
            foreach (var jogo in cfg.Jogos)
            {
                var estado = EstadoJogo.Carregar(jogo.Appid) ?? CriarPlano(jogo, cfg, rng);
                Nomes[jogo.Appid] = estado?.Nome ?? jogo.Nome ?? jogo.Appid.ToString();
                if (estado != null && !estado.Concluido) pendentes.Add(jogo.Appid);
                else if (cfg.ManterAberto) ociosos.Add(jogo.Appid);
            }

            // Os jogos com conquistas a liberar têm prioridade nas vagas de jogos_simultaneos
            var desejados = DentroDaJanela(cfg, DateTime.Now)
                ? pendentes.Concat(ociosos).Where(Liberado).Take(cfg.JogosSimultaneos > 0 ? cfg.JogosSimultaneos : int.MaxValue).ToList()
                : new List<uint>();

            foreach (var appid in Sessoes.Keys.Except(desejados).ToList())
            {
                Log.Escrever($"fechando sessão de {appid} (fora da janela ou fora da lista)");
                try { Sessoes[appid].Kill(); } catch (InvalidOperationException) { }
                Sessoes.Remove(appid);
            }

            foreach (var appid in desejados.Where(a => !Sessoes.ContainsKey(a)))
            {
                AbrirSessao(appid);
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
                return null;
            }
        }

        private static void AbrirSessao(uint appid)
        {
            var info = new ProcessStartInfo
            {
                FileName = ProcessoSessao.Preparar(Process.GetCurrentProcess().MainModule.FileName, Nomes[appid]),
                Arguments = $"--sessao {appid} --pai {Process.GetCurrentProcess().Id}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Sessoes[appid] = Process.Start(info);
            Log.Escrever($"sessão aberta para {appid}");
        }

        private static void RecolherSessoesEncerradas()
        {
            foreach (var par in Sessoes.Where(p => p.Value.HasExited).ToList())
            {
                var codigo = par.Value.ExitCode;
                Sessoes.Remove(par.Key);
                if (codigo == CodigoEncerradaPorFora)
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
