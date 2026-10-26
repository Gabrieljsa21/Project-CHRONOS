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
    }

    internal sealed class EstadoJogo
    {
        public uint Appid { get; set; }
        public string Nome { get; set; }
        public string Fonte { get; set; }
        public double DuracaoMinutos { get; set; }
        public double MinutosJogados { get; set; }
        public DateTime CriadoEm { get; set; }
        // Falso até a primeira sessão conferir com a Steam o que o usuário já tinha e refazer
        // o cronograma só com o que falta
        public bool Sincronizado { get; set; }
        public List<Conquista> Conquistas { get; set; } = new List<Conquista>();

        public bool Concluido => Conquistas.All(c => c.Situacao != Situacao.Pendente);

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
            // Troca atômica: uma sessão encerrada no meio da gravação nunca deixa o arquivo pela metade
            if (File.Exists(caminho)) File.Replace(temporario, caminho, null);
            else File.Move(temporario, caminho);
        }
    }
}
