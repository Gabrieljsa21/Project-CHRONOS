using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace Chronos
{
    // Monta o cronograma de desbloqueio de um jogo a partir de dados públicos
    internal static class Planejador
    {
        private static readonly HttpClient Http = CriarHttp();

        private static HttpClient CriarHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Project-CHRONOS/0.1");
            return http;
        }

        public static EstadoJogo Planejar(JogoConfig jogo, Config cfg, Random rng)
        {
            var estado = new EstadoJogo { Appid = jogo.Appid, Nome = jogo.Nome, CriadoEm = DateTime.Now };
            double? medianaMinutos = null;

            try
            {
                medianaMinutos = CarregarSteamHunters(estado);
                estado.Fonte = "steamhunters";
            }
            catch (Exception e)
            {
                Log.Escrever($"SteamHunters indisponível para {jogo.Appid} ({e.Message}); usando porcentagens da Steam");
                CarregarSteam(estado);
                estado.Fonte = "steam";
            }

            if (estado.Conquistas.Count == 0)
            {
                throw new InvalidOperationException($"nenhuma conquista obtível encontrada para o appid {jogo.Appid}");
            }

            var horas = jogo.DuracaoHoras
                ?? (medianaMinutos > 0 ? medianaMinutos.Value / 60.0 : cfg.DuracaoPadraoHoras);
            estado.DuracaoMinutos = horas * 60.0 * cfg.FatorDuracao;
            DistribuirTempos(estado.Conquistas, estado.DuracaoMinutos, cfg, rng);
            return estado;
        }

        // Retorna o tempo mediano para 100% em minutos
        private static double? CarregarSteamHunters(EstadoJogo estado)
        {
            using (var app = BaixarJson($"https://steamhunters.com/api/apps/{estado.Appid}"))
            using (var lista = BaixarJson($"https://steamhunters.com/api/apps/{estado.Appid}/achievements"))
            {
                var raiz = app.RootElement;
                if (string.IsNullOrEmpty(estado.Nome) && raiz.TryGetProperty("name", out var nome))
                {
                    estado.Nome = nome.GetString();
                }

                foreach (var item in lista.RootElement.EnumerateArray())
                {
                    // obtainability diferente de 0 marca conquista impossível de obter
                    if (item.TryGetProperty("obtainability", out var obt) && obt.GetInt32() != 0) continue;
                    estado.Conquistas.Add(new Conquista
                    {
                        ApiName = item.GetProperty("apiName").GetString(),
                        Nome = item.GetProperty("name").GetString(),
                        Porcentagem = item.GetProperty("steamPercentage").GetDouble(),
                    });
                }

                if (raiz.TryGetProperty("medianCompletionTime", out var mediana) && mediana.ValueKind == JsonValueKind.Number)
                {
                    return mediana.GetDouble();
                }
                return null;
            }
        }

        private static void CarregarSteam(EstadoJogo estado)
        {
            var url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={estado.Appid}";
            using (var doc = BaixarJson(url))
            {
                var itens = doc.RootElement.GetProperty("achievementpercentages").GetProperty("achievements");
                foreach (var item in itens.EnumerateArray())
                {
                    var api = item.GetProperty("name").GetString();
                    var pct = item.GetProperty("percent");
                    // A API já devolveu o campo como número e como texto, conforme a época
                    var valor = pct.ValueKind == JsonValueKind.String
                        ? double.Parse(pct.GetString(), CultureInfo.InvariantCulture)
                        : pct.GetDouble();
                    // O nome de exibição é preenchido pela sessão, que lê o schema local da Steam
                    estado.Conquistas.Add(new Conquista { ApiName = api, Nome = api, Porcentagem = valor });
                }
            }
            if (string.IsNullOrEmpty(estado.Nome)) estado.Nome = estado.Appid.ToString();
        }

        private static JsonDocument BaixarJson(string url)
        {
            var resposta = Http.GetAsync(url).GetAwaiter().GetResult();
            resposta.EnsureSuccessStatusCode();
            return JsonDocument.Parse(resposta.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        // Comuns primeiro, raras no fim. A posição de cada conquista na duração total segue
        // ln(pTopo / p) normalizado e elevado à curva: valores acima de 1 aproximam as comuns
        // do início, já que a mediana de 100% é dominada pelo tempo gasto nas raras.
        internal static void DistribuirTempos(List<Conquista> conquistas, double duracaoMinutos, Config cfg, Random rng)
        {
            var ordenadas = conquistas.OrderByDescending(c => c.Porcentagem).ToList();
            var n = ordenadas.Count;
            var pTopo = Math.Max(0.01, ordenadas[0].Porcentagem);
            var pBase = Math.Max(0.01, ordenadas[n - 1].Porcentagem);
            var amplitude = Math.Log(pTopo / pBase);

            var tempos = new List<double>(n);
            for (var i = 0; i < n; i++)
            {
                double fracao;
                if (amplitude < 1e-6)
                {
                    // Todas com a mesma raridade: distribui por posição
                    fracao = n == 1 ? 1.0 : (double)i / (n - 1);
                }
                else
                {
                    fracao = Math.Log(pTopo / Math.Max(0.01, ordenadas[i].Porcentagem)) / amplitude;
                }
                fracao = 0.02 + 0.98 * Math.Pow(fracao, cfg.Curva);
                var ruido = 1.0 + (rng.NextDouble() * 2.0 - 1.0) * cfg.Variacao;
                tempos.Add(duracaoMinutos * fracao * ruido);
            }

            // O ruído pode inverter vizinhas; reordenar mantém comuns antes de raras
            tempos.Sort();
            var anterior = 0.0;
            for (var i = 0; i < n; i++)
            {
                var minuto = Math.Max(tempos[i], anterior + cfg.IntervaloMinimoMinutos);
                ordenadas[i].Minuto = Math.Round(minuto, 1);
                anterior = minuto;
            }
        }
    }
}
