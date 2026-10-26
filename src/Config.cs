using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Chronos
{
    internal sealed class Janela
    {
        // Vazio = todos os dias. Códigos: dom, seg, ter, qua, qui, sex, sab
        public List<string> Dias { get; set; } = new List<string>();
        public string Inicio { get; set; } = "00:00";
        // Fim menor ou igual ao início atravessa a meia-noite (ex.: 20:00 até 02:00)
        public string Fim { get; set; } = "24:00";
    }

    internal sealed class JogoConfig
    {
        public uint Appid { get; set; }
        public string Nome { get; set; }
        // Sobrescreve a duração vinda do SteamHunters
        public double? DuracaoHoras { get; set; }
    }

    internal sealed class Config
    {
        public string PastaSam { get; set; } = @"E:\Downloads\Jogos & Steam\SteamAchievementManager-7.0.41";
        public bool Simulacao { get; set; }
        // Mostra "Jogando <jogo>" no Discord enquanto a sessão está aberta
        public bool Discord { get; set; } = true;
        public int JogosSimultaneos { get; set; } = 1;
        // Jogo da lista sem conquistas a liberar (concluído ou sem conquistas) continua aberto
        // na Steam, só para contar horas, como o ArchiSteamFarm faz
        public bool ManterAberto { get; set; }
        public double FatorDuracao { get; set; } = 1.0;
        public double DuracaoPadraoHoras { get; set; } = 50;
        public double Curva { get; set; } = 1.5;
        public double Variacao { get; set; } = 0.15;
        // Quanto a ordem foge da raridade: 0 = sempre da mais comum para a mais rara
        public double Desordem { get; set; } = 0.0;
        // Espaço entre duas conquistas seguidas: sorteado entre o mínimo e o máximo
        public double IntervaloMinimoMinutos { get; set; } = 15;
        public double IntervaloMaximoMinutos { get; set; } = 30;
        public List<Janela> Janelas { get; set; } = new List<Janela>();
        public List<JogoConfig> Jogos { get; set; } = new List<JogoConfig>();

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };

        public static Config Carregar()
        {
            if (!File.Exists(Caminhos.Config))
            {
                var exemplo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.exemplo.json");
                if (File.Exists(exemplo))
                {
                    File.Copy(exemplo, Caminhos.Config);
                }
                else
                {
                    File.WriteAllText(Caminhos.Config, JsonSerializer.Serialize(new Config(), Json), new UTF8Encoding(false));
                }
                Log.Escrever($"config.json criado em {Caminhos.Config}");
            }
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(Caminhos.Config), Json) ?? new Config();
        }
    }

    internal static class Caminhos
    {
        public static readonly string Dados = Environment.GetEnvironmentVariable("CHRONOS_DADOS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chronos");

        public static string Config => Path.Combine(Dados, "config.json");
        public static string Log => Path.Combine(Dados, "chronos.log");
        public static string Estados => Path.Combine(Dados, "estado");

        public static void Garantir()
        {
            Directory.CreateDirectory(Dados);
            Directory.CreateDirectory(Estados);
        }
    }
}
