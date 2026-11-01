using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chronos
{
    // Tempo de 100% ("Completionist") do HowLongToBeat. O site não tem API pública: a busca do
    // próprio site pede um token (GET /api/search/site/init) e recusa User-Agent que não seja de
    // navegador. A busca é pelo nome; o jogo só vale se a página dele aponta para o mesmo appid
    // da Steam (profile_steam), para um homônimo nunca entrar.
    internal static class HowLongToBeat
    {
        private const string Site = "https://howlongtobeat.com";
        private static readonly HttpClient Http = CriarHttp();

        private static HttpClient CriarHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            http.DefaultRequestHeaders.Referrer = new Uri(Site + "/");
            return http;
        }

        // Minutos para 100%; null quando o site não tem o jogo ou não tem tempo de 100% dele.
        // Lança exceção quando o site falha, para quem chama tentar de novo depois.
        public static double? Minutos100(uint appid, string nome)
        {
            var termos = Normalizar(nome);
            if (termos.Length == 0) return null;

            string token;
            using (var init = Json(Http.GetAsync($"{Site}/api/search/site/init?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}")))
            {
                token = init.RootElement.GetProperty("token").GetString();
            }

            var corpo = JsonSerializer.Serialize(new
            {
                searchType = "games",
                searchTerms = termos,
                searchPage = 1,
                size = 20,
                searchOptions = new
                {
                    games = new
                    {
                        userId = 0,
                        platform = "",
                        sortCategory = "popular",
                        rangeCategory = "main",
                        rangeTime = new { min = 0, max = 0 },
                        gameplay = new { perspective = "", flow = "", genre = "" },
                        year = "",
                        modifier = "",
                    },
                    users = new { sortCategory = "postcount" },
                    lists = new { sortCategory = "follows" },
                    filter = "",
                    sort = 0,
                    randomizer = 0,
                },
                useCache = true,
            });
            var pedido = new HttpRequestMessage(HttpMethod.Post, $"{Site}/api/search/site")
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
            };
            pedido.Headers.Add("x-auth-token", token);

            var candidatos = new List<(int Id, string Nome)>();
            using (var busca = Json(Http.SendAsync(pedido)))
            {
                foreach (var item in busca.RootElement.GetProperty("data").EnumerateArray())
                {
                    candidatos.Add((item.GetProperty("game_id").GetInt32(), item.GetProperty("game_name").GetString()));
                }
            }

            // Nome igual primeiro; a página de cada um diz o appid. Até 5 páginas por jogo
            var alvo = string.Join(" ", termos);
            foreach (var (id, _) in candidatos.OrderBy(c => string.Join(" ", Normalizar(c.Nome)) == alvo ? 0 : 1).Take(5))
            {
                var jogo = Pagina(id);
                if (jogo == null || !jogo.Value.TryGetProperty("profile_steam", out var steam) || steam.GetInt64() != appid) continue;
                var segundos = jogo.Value.TryGetProperty("comp_100", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0;
                return segundos > 0 ? segundos / 60.0 : (double?)null;
            }
            return null;
        }

        // Dados do jogo no __NEXT_DATA__ da página /game/<id>
        private static JsonElement? Pagina(int id)
        {
            var html = Http.GetStringAsync($"{Site}/game/{id}").GetAwaiter().GetResult();
            var m = Regex.Match(html, "<script id=\"__NEXT_DATA__\"[^>]*>(.*?)</script>", RegexOptions.Singleline);
            if (!m.Success) return null;
            using (var doc = JsonDocument.Parse(m.Groups[1].Value))
            {
                if (!doc.RootElement.TryGetProperty("props", out var props)
                    || !props.TryGetProperty("pageProps", out var page)
                    || !page.TryGetProperty("game", out var game)
                    || !game.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("game", out var lista)
                    || lista.GetArrayLength() == 0) return null;
                return lista[0].Clone();
            }
        }

        // Palavras do nome em minúsculas, sem ™, ®, pontuação e acentos
        private static string[] Normalizar(string nome)
        {
            // Apóstrofo some sem virar espaço: "THAT'S" busca "thats", como o site faz
            var semAcento = new string(Regex.Replace(nome ?? "", "['’]", "").Normalize(NormalizationForm.FormD)
                .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray());
            return Regex.Replace(semAcento.ToLowerInvariant(), "[^a-z0-9]+", " ")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static JsonDocument Json(System.Threading.Tasks.Task<HttpResponseMessage> tarefa)
        {
            var resposta = tarefa.GetAwaiter().GetResult();
            resposta.EnsureSuccessStatusCode();
            return JsonDocument.Parse(resposta.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }
    }
}
