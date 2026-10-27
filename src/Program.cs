using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Chronos
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int pid);

        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Caminhos.Garantir();
            Config cfg;
            try
            {
                cfg = Config.Carregar();
            }
            catch (Exception e)
            {
                // WinExe não mostra erro na tela: sem esta linha o programa fecharia em silêncio
                Log.Escrever($"config.json inválido ({Caminhos.Config}): {e.Message}");
                // Não 1: para o agendador, sessão que sai com 1 foi encerrada pelo Gerenciador de Tarefas
                return 5;
            }

            // SAM.API.dll não acompanha o Chronos: vem da pasta do SAM definida no config.json
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
                new AssemblyName(e.Name).Name == "SAM.API"
                    ? Assembly.LoadFrom(Path.Combine(cfg.PastaSam, "SAM.API.dll"))
                    : null;

            var comando = args.Length > 0 ? args[0] : "";
            switch (comando)
            {
                case "--sessao":
                    return Sessao.Executar(uint.Parse(args[1]), Argumento(args, "--pai"), cfg);
                case "--status":
                    AnexarConsole();
                    Comandos.Status(cfg);
                    return 0;
                case "--plano":
                    AnexarConsole();
                    Comandos.Plano(cfg, uint.Parse(args[1]));
                    return 0;
                case "--biblioteca":
                    AnexarConsole();
                    return Biblioteca.Executar();
                case "":
                    return Agendador.Executar();
                default:
                    AnexarConsole();
                    Console.WriteLine("uso: Chronos.exe [--status | --plano <appid> | --biblioteca]");
                    return 1;
            }
        }

        private static int Argumento(string[] args, string nome)
        {
            var i = Array.IndexOf(args, nome);
            return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : 0;
        }

        // WinExe não tem console próprio: usa o do terminal que chamou ou a saída redirecionada
        private static void AnexarConsole()
        {
            AttachConsole(-1);
            var saida = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(saida);
            Console.WriteLine();
        }
    }

    internal static class Comandos
    {
        public static void Status(Config cfg)
        {
            Console.WriteLine($"Dados: {Caminhos.Dados}");
            Console.WriteLine($"Dentro da janela agora: {(Agendador.DentroDaJanela(cfg, DateTime.Now) ? "sim" : "não")}{(cfg.Simulacao ? "  [SIMULAÇÃO]" : "")}");
            foreach (var jogo in cfg.Jogos)
            {
                var estado = EstadoJogo.Carregar(jogo.Appid);
                if (estado == null)
                {
                    Console.WriteLine($"- {jogo.Nome ?? jogo.Appid.ToString()} ({jogo.Appid}): sem plano ainda{(cfg.ManterAberto ? "; fica aberto só para contar horas" : "")}");
                    continue;
                }
                var feitas = estado.Conquistas.Count(c => c.Situacao == Situacao.Desbloqueada || c.Situacao == Situacao.JaTinha);
                var semDlc = estado.Conquistas.Count(c => c.Situacao == Situacao.SemDlc);
                var recusadas = estado.Conquistas.Count(c => c.Recusada);
                Console.WriteLine($"- {estado.Nome} ({estado.Appid}): {feitas}/{estado.Conquistas.Count} conquistas{(semDlc > 0 ? $" ({semDlc} sem DLC)" : "")}{(recusadas > 0 ? $" ({recusadas} recusadas pela Steam, nova tentativa na próxima sessão)" : "")}, {Horas(estado.MinutosJogados)} de {Horas(estado.DuracaoMinutos)} jogadas");
                var proxima = estado.Pendentes.FirstOrDefault();
                if (estado.Concluido && cfg.ManterAberto) Console.WriteLine("    concluído; fica aberto só para contar horas");
                if (proxima != null)
                {
                    Console.WriteLine($"    próxima: {proxima.Nome} ({proxima.Porcentagem:F1}%) em {Horas(Math.Max(0, proxima.Minuto - estado.MinutosJogados))} de jogo");
                }
            }
        }

        // Mostra um cronograma de exemplo sem salvar nada
        public static void Plano(Config cfg, uint appid)
        {
            var jogo = cfg.Jogos.FirstOrDefault(j => j.Appid == appid) ?? new JogoConfig { Appid = appid };
            var estado = Planejador.Planejar(jogo, cfg, new Random());
            var semDlc = ConquistasSemDlc(appid);
            Console.WriteLine($"{estado.Nome} ({appid}), fonte {estado.Fonte}: {estado.Conquistas.Count} conquistas em {Horas(estado.DuracaoMinutos)}{(semDlc.Count > 0 ? $", {semDlc.Count} de DLC que a conta não tem" : "")}");
            foreach (var c in estado.Conquistas.OrderBy(c => c.Minuto))
            {
                Console.WriteLine($"  {Horas(c.Minuto),8}  {c.Porcentagem,5:F1}%  {c.Nome}{(semDlc.Contains(c.ApiName) ? "  [sem DLC, fica de fora]" : "")}");
            }
        }

        // Mesma regra da sessão; vazio se a Steam estiver fechada
        private static HashSet<string> ConquistasSemDlc(uint appid)
        {
            var dlcs = EsquemaSteam.ConquistasDeDlc(appid);
            if (dlcs.Count == 0) return new HashSet<string>();
            using (var cliente = new SAM.API.Client())
            {
                try
                {
                    cliente.Initialize(0);
                }
                catch (SAM.API.ClientInitializeException)
                {
                    return new HashSet<string>();
                }
                return new HashSet<string>(dlcs.Where(p => !cliente.SteamApps008.IsSubscribedApp(p.Value)).Select(p => p.Key));
            }
        }

        private static string Horas(double minutos) => $"{(int)(minutos / 60)}h{(int)(minutos % 60):D2}";
    }
}
