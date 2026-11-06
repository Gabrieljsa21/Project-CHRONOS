using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        // Quando os espaços não cabem na duração (muitas conquistas para pouco tempo), encolhem
        // na mesma proporção até o mínimo chegar a este piso; o máximo fica no dobro. Conquistas
        // marcadas a menos de 2 min uma da outra saem em rajada (Sessao)
        public double IntervaloPisoMinutos { get; set; } = 0.5;
        // O espaço não passa desta fração do tempo médio por conquista (duração / conquistas):
        // jogo curto com muitas conquistas tem espaços curtos, jogo longo fica nos 15 a 30 min
        public double IntervaloFracao { get; set; } = 0.25;
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
            var texto = File.ReadAllText(Caminhos.Config);
            Config cfg;
            try
            {
                cfg = Interpretar(texto);
            }
            catch (JsonException e)
            {
                return Restaurar(e);
            }
            GuardarCopia(texto);
            return cfg;
        }

        private static Config Interpretar(string texto) =>
            JsonSerializer.Deserialize<Config>(texto, Json) ?? throw new JsonException("config.json vazio");

        // Cópia do último config.json bom, regravada só quando ele muda. Todo processo do
        // CHRONOS chega aqui (agendador a cada ciclo, cada sessão ao abrir): dois podem
        // gravar ao mesmo tempo, por isso o temporário é por processo e a falha é ignorada
        private static void GuardarCopia(string texto)
        {
            try
            {
                if (File.Exists(Caminhos.ConfigCopia) && File.ReadAllText(Caminhos.ConfigCopia) == texto) return;
                Arquivo.GravarAtomico(Caminhos.ConfigCopia, texto, $"{Caminhos.ConfigCopia}.{Process.GetCurrentProcess().Id}.tmp");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }

        // config.json corrompido (06/11/2026: uma queda de energia deixou o arquivo cheio de
        // 0x00 e o CHRONOS recusou subir até alguém refazer a lista de jogos à mão): volta para
        // a cópia e guarda o arquivo estragado ao lado, para conferência
        private static Config Restaurar(JsonException erro)
        {
            if (!File.Exists(Caminhos.ConfigCopia)) throw erro;
            var texto = File.ReadAllText(Caminhos.ConfigCopia);
            Config cfg;
            try
            {
                cfg = Interpretar(texto);
            }
            catch (JsonException)
            {
                throw new JsonException($"{erro.Message} (a cópia config.json.bak também está inválida)", erro);
            }
            try
            {
                File.Copy(Caminhos.Config, $"{Caminhos.Config}.corrompido-{DateTime.Now:yyyyMMdd-HHmmss}", false);
                Arquivo.GravarAtomico(Caminhos.Config, texto, $"{Caminhos.Config}.{Process.GetCurrentProcess().Id}.tmp");
                Log.Escrever($"config.json corrompido ({erro.Message}); restaurado da cópia de {File.GetLastWriteTime(Caminhos.ConfigCopia):dd/MM HH:mm}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Outro processo do CHRONOS restaurou no mesmo instante; a cópia já serve
            }
            return cfg;
        }

        // Regrava só a ordem de "jogos", com o resto do arquivo como está (a GAIA também grava
        // nele). Troca atômica, como o estado: o agendador e a GAIA nunca leem pela metade
        public static void SalvarOrdemJogos(IList<uint> ordem)
        {
            var raiz = JsonNode.Parse(File.ReadAllText(Caminhos.Config), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }).AsObject();
            var jogos = raiz["jogos"].AsArray();
            var itens = jogos.ToList();
            jogos.Clear();
            foreach (var item in itens.OrderBy(i => { var p = ordem.IndexOf(i["appid"].GetValue<uint>()); return p < 0 ? int.MaxValue : p; }))
            {
                jogos.Add(item);
            }

            Arquivo.GravarAtomico(Caminhos.Config, raiz.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        }
    }

    internal static class Caminhos
    {
        public static readonly string Dados = Environment.GetEnvironmentVariable("CHRONOS_DADOS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chronos");

        public static string Config => Path.Combine(Dados, "config.json");
        // Última versão do config.json que abriu sem erro (Config.Carregar)
        public static string ConfigCopia => Path.Combine(Dados, "config.json.bak");
        public static string Log => Path.Combine(Dados, "chronos.log");
        public static string Estados => Path.Combine(Dados, "estado");

        public static void Garantir()
        {
            Directory.CreateDirectory(Dados);
            Directory.CreateDirectory(Estados);
        }
    }
}
