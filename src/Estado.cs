using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Chronos
{
    internal static class Situacao
    {
        public const string Pendente = "pendente";
        public const string Desbloqueada = "desbloqueada";
        public const string JaTinha = "ja_tinha";
        public const string Ignorada = "ignorada";
        // Conquista de um Achievement Set de DLC que a conta não tem: a Steam recusaria
        public const string SemDlc = "sem_dlc";
        // Só da 0.5.1: a Steam recusou 4 vezes. Desde a 0.6.0 a recusa segura a fila (ParadoEm),
        // e a sincronização devolve estas a pendentes
        public const string Recusada = "recusada";
    }

    internal sealed class Conquista
    {
        public string ApiName { get; set; }
        public string Nome { get; set; }
        public double Porcentagem { get; set; }
        // Minuto "jogado" (contado pelo Chronos) em que a conquista sai
        public double Minuto { get; set; }
        public string Situacao { get; set; } = Chronos.Situacao.Pendente;
        public int Falhas { get; set; }
        public bool Simulada { get; set; }
        public DateTime? DesbloqueadaEm { get; set; }

        internal bool Recusada => Situacao == Chronos.Situacao.Recusada || (Situacao == Chronos.Situacao.Ignorada && Falhas > 0);
    }

    internal sealed class EstadoJogo
    {
        public uint Appid { get; set; }
        public string Nome { get; set; }
        public string Fonte { get; set; }
        public double DuracaoMinutos { get; set; }
        // Duração do jogo inteiro, do zero ao 100%; DuracaoMinutos é onde o cronograma termina.
        // 0 nos planos da 0.7.0 e anteriores, que a recalculam na próxima sessão
        public double DuracaoTotalMinutos { get; set; }
        public double MinutosJogados { get; set; }
        // Horas que a Steam tinha registrado quando o cronograma foi refeito; null nos planos
        // da 0.7.0 e anteriores, que são refeitos uma vez para contar essas horas
        public double? MinutosSteam { get; set; }
        public DateTime CriadoEm { get; set; }
        // Falso até a primeira sessão conferir com a Steam o que o usuário já tinha e refazer
        // o cronograma só com o que falta
        public bool Sincronizado { get; set; }
        public List<Conquista> Conquistas { get; set; } = new List<Conquista>();
        // apiName das conquistas do schema que o SteamHunters marca como impossíveis: ficam fora
        // do plano e não contam como conquista nova do jogo
        public List<string> Descartadas { get; set; } = new List<string>();
        // A Steam recusou a conquista ParadoNa (api_name) por 1 h: nada mais sai até --retomar
        public DateTime? ParadoEm { get; set; }
        public string ParadoNa { get; set; }

        public bool Concluido => Conquistas.All(c => c.Situacao != Situacao.Pendente);

        internal bool Parado => ParadoEm != null;

        // Último desbloqueio; null quando todas já existiam na conta antes do CHRONOS
        internal DateTime? ConcluidoEm => Conquistas.Max(c => c.DesbloqueadaEm);

        // Inclui as "ignorada" com falhas, que é como a 0.5.0 e anteriores guardavam as recusadas
        internal bool TemRecusadas => Conquistas.Any(c => c.Recusada);

        public IEnumerable<Conquista> Pendentes =>
            Conquistas.Where(c => c.Situacao == Situacao.Pendente).OrderBy(c => c.Minuto);

        public static string Caminho(uint appid) => Path.Combine(Caminhos.Estados, $"{appid}.json");

        public static EstadoJogo Carregar(uint appid)
        {
            var caminho = Caminho(appid);
            if (!File.Exists(caminho)) return null;
            // A sessão pode estar substituindo o arquivo no mesmo instante
            for (var tentativa = 0; ; tentativa++)
            {
                try
                {
                    return JsonSerializer.Deserialize<EstadoJogo>(File.ReadAllText(caminho), Config.Json);
                }
                catch (IOException) when (tentativa < 5)
                {
                    Thread.Sleep(100);
                }
            }
        }

        public void Salvar()
        {
            var caminho = Caminho(Appid);
            var temporario = caminho + ".tmp";
            File.WriteAllText(temporario, JsonSerializer.Serialize(this, Config.Json), new UTF8Encoding(false));
            // Troca atômica: uma sessão encerrada no meio da gravação nunca deixa o arquivo pela metade.
            // A troca falha enquanto a GAIA ou o agendador leem o arquivo
            for (var tentativa = 0; ; tentativa++)
            {
                try
                {
                    if (File.Exists(caminho)) File.Replace(temporario, caminho, null);
                    else File.Move(temporario, caminho);
                    return;
                }
                catch (IOException) when (tentativa < 10)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
