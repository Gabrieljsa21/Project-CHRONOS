using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chronos
{
    // Tempo de 100% do curador "Average Achievement Hunter" da loja da Steam: cada análise
    // termina em "100% time: ~20-25h" (ou "Time to 100%: ..."). O endpoint da loja ignora a
    // busca por nome, então a lista inteira (cerca de 1.200 jogos, 100 por página) vai para
    // curador_aah.json e é baixada de novo depois de 7 dias.
    internal static class CuradorSteam
    {
        private const int Curador = 30168383;
        private static readonly TimeSpan Validade = TimeSpan.FromDays(7);
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static Dictionary<uint, double> lista;

        private static string Cache => Path.Combine(Caminhos.Dados, "curador_aah.json");

        // Minutos para 100%; null quando o curador não analisou o jogo ou não deu o tempo.
        // Lança exceção quando a Steam falha e não há cache.
        public static double? Minutos100(uint appid)
        {
            lista = lista ?? Carregar();
            return lista.TryGetValue(appid, out var minutos) ? minutos : (double?)null;
        }

        private static Dictionary<uint, double> Carregar()
        {
            var temCache = File.Exists(Cache);
            if (temCache && DateTime.Now - File.GetLastWriteTime(Cache) < Validade) return LerCache();
            try
            {
                var baixada = Baixar();
                var temporario = Cache + ".tmp";
                File.WriteAllText(temporario, JsonSerializer.Serialize(baixada.ToDictionary(p => p.Key.ToString(), p => p.Value)), new UTF8Encoding(false));
                if (temCache) File.Replace(temporario, Cache, null); else File.Move(temporario, Cache);
                return baixada;
            }
            catch (Exception e) when (temCache)
            {
                Log.Escrever($"curador da Steam indisponível ({e.Message}); usando a lista de {File.GetLastWriteTime(Cache):dd/MM}");
                return LerCache();
            }
        }

        private static Dictionary<uint, double> LerCache() =>
            JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(Cache))
                .ToDictionary(p => uint.Parse(p.Key), p => p.Value);

        private static Dictionary<uint, double> Baixar()
        {
            var resultado = new Dictionary<uint, double>();
            for (int inicio = 0, total = 1; inicio < total; inicio += 100)
            {
                var url = $"https://store.steampowered.com/curator/{Curador}/ajaxgetfilteredrecommendations/render/?query=&start={inicio}&count=100&sort=recent&types=0";
                var resposta = Http.GetAsync(url).GetAwaiter().GetResult();
                resposta.EnsureSuccessStatusCode();
                using (var doc = JsonDocument.Parse(resposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()))
                {
                    total = doc.RootElement.GetProperty("total_count").GetInt32();
                    var html = doc.RootElement.GetProperty("results_html").GetString();
                    foreach (var bloco in html.Split(new[] { "class=\"recommendation\"" }, StringSplitOptions.None).Skip(1))
                    {
                        var appid = Regex.Match(bloco, "data-ds-appid=\"(\\d+)\"");
                        var texto = Regex.Match(bloco, "class=\"recommendation_desc\">(.*?)</div>", RegexOptions.Singleline);
                        if (!appid.Success || !texto.Success) continue;
                        var minutos = Interpretar(System.Net.WebUtility.HtmlDecode(texto.Groups[1].Value));
                        if (minutos > 0) resultado[uint.Parse(appid.Groups[1].Value)] = minutos.Value;
                    }
                }
            }
            return resultado;
        }

        // "~20-25h" vira a média da faixa; "15h+", "<2h" e "up to 3h" ficam com o número;
        // "25 minutes" em minutos. Só o trecho até a primeira unidade conta ("~5h if you skip
        // text, ~8h+ if you read all" vale 5 h)
        internal static double? Interpretar(string descricao)
        {
            var m = Regex.Match(descricao, @"(?:100% time|time to 100%)\s*:\s*(.+)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var trecho = Regex.Match(m.Groups[1].Value, @"^[^\d]{0,12}(\d+(?:[.,]\d+)?)\s*(h|hours?|minutes?|min)?\s*(?:(?:-|–|to)\s*(\d+(?:[.,]\d+)?)\s*(h|hours?|minutes?|min)?)?", RegexOptions.IgnoreCase);
            if (!trecho.Success || !double.TryParse(trecho.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var de)) return null;
            var ate = double.TryParse(trecho.Groups[3].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : de;
            var unidade = trecho.Groups[4].Success ? trecho.Groups[4].Value : trecho.Groups[2].Value;
            var emMinutos = unidade.StartsWith("min", StringComparison.OrdinalIgnoreCase);
            var valor = (de + ate) / 2.0;
            return emMinutos ? valor : valor * 60.0;
        }
    }
}
