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
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Project-CHRONOS/0.2");
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

            estado.DuracaoTotalMinutos = Duracao(jogo, medianaMinutos, cfg);
            estado.DuracaoMinutos = estado.DuracaoTotalMinutos;
            DistribuirTempos(estado.Conquistas, estado.DuracaoMinutos, cfg, rng);
            return estado;
        }

        // duracao_horas do config.json vence a mediana do SteamHunters
        private static double Duracao(JogoConfig jogo, double? medianaMinutos, Config cfg)
        {
            var horas = jogo?.DuracaoHoras
                ?? (medianaMinutos > 0 ? medianaMinutos.Value / 60.0 : cfg.DuracaoPadraoHoras);
            return horas * 60.0 * cfg.FatorDuracao;
        }

        // Duração atual do jogo para um plano que já existe: refaz a conta só quando o
        // config.json manda (duracao_horas) ou quando o plano é anterior ao campo
        internal static double DuracaoAtual(EstadoJogo estado, JogoConfig jogo, Config cfg)
        {
            if (jogo?.DuracaoHoras == null && estado.DuracaoTotalMinutos > 0) return estado.DuracaoTotalMinutos;

            double? mediana = null;
            if (jogo?.DuracaoHoras == null && estado.Fonte == "steamhunters")
            {
                try
                {
                    using (var app = BaixarJson($"https://steamhunters.com/api/apps/{estado.Appid}"))
                    {
                        if (app.RootElement.TryGetProperty("medianCompletionTime", out var m) && m.ValueKind == JsonValueKind.Number)
                        {
                            mediana = m.GetDouble();
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Escrever($"SteamHunters indisponível para {estado.Appid} ({e.Message}); duração atual mantida");
                    return estado.DuracaoMinutos;
                }
            }
            return Duracao(jogo, mediana, cfg);
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

        // Quanto mais rara, mais demora: a espera até cada conquista (desde a anterior) é um
        // espaço sorteado entre intervalo_minimo e intervalo_maximo mais uma parte da duração
        // proporcional ao peso de dificuldade dela, ln(100 / p)^curva. Os espaços saem da
        // duração antes da divisão, então o total continua sendo a duração do jogo. Uma de 60%
        // sai pouco depois do espaço mínimo; uma de 2% leva horas. Para o ritmo não ter padrão, o
        // espaço é sorteado e a parte de dificuldade tem ruído de ±variacao. A ordem segue a
        // raridade; `desordem` acima de 0 soma um desvio gaussiano a ela (uma rara pode vir antes
        // de uma comum e ainda demora o tempo dela).
        internal static void DistribuirTempos(List<Conquista> conquistas, double duracaoMinutos, Config cfg, Random rng)
        {
            var n = conquistas.Count;
            var dificuldade = conquistas.Select(c => Dificuldade(c.Porcentagem)).ToList();
            var amplitude = Math.Max(1e-6, dificuldade.Max() - dificuldade.Min());
            var ordem = Enumerable.Range(0, n)
                .OrderBy(i => dificuldade[i] + Gaussiano(rng) * cfg.Desordem * amplitude)
                .ToList();

            var pesos = dificuldade.Select(d => Peso(d, cfg)).ToList();
            var minimo = Math.Min(cfg.IntervaloMinimoMinutos, cfg.IntervaloMaximoMinutos);
            var maximo = Math.Max(cfg.IntervaloMinimoMinutos, cfg.IntervaloMaximoMinutos);
            var espacos = Enumerable.Range(0, n).Select(_ => minimo + rng.NextDouble() * (maximo - minimo)).ToList();
            // Duração curta demais para os espaços: sobra só o espaço sorteado
            var escala = Math.Max(0.0, duracaoMinutos - espacos.Sum()) / pesos.Sum();

            var minuto = 0.0;
            foreach (var i in ordem)
            {
                var ruido = 1.0 + (rng.NextDouble() * 2.0 - 1.0) * cfg.Variacao;
                minuto += espacos[i] + pesos[i] * escala * ruido;
                conquistas[i].Minuto = Math.Round(minuto, 1);
            }
        }

        // Refaz o cronograma só com as pendentes, a partir do tempo já jogado. O plano termina na
        // duração total: a duração restante é o que falta dela depois das horas já jogadas, e
        // quem já passou da duração total recebe só os espaços entre as conquistas. Sem horas
        // jogadas (Steam sem registro), fica a fatia equivalente ao peso das pendentes, para quem
        // já tinha as comuns não esperar por elas de novo.
        internal static void Replanejar(EstadoJogo estado, Config cfg, Random rng)
        {
            var pendentes = estado.Conquistas.Where(c => c.Situacao == Situacao.Pendente).ToList();
            if (pendentes.Count == 0) return;
            var total = estado.DuracaoTotalMinutos > 0 ? estado.DuracaoTotalMinutos : estado.DuracaoMinutos;
            var pesoTotal = estado.Conquistas.Sum(c => Peso(Dificuldade(c.Porcentagem), cfg));
            var pesoPendente = pendentes.Sum(c => Peso(Dificuldade(c.Porcentagem), cfg));
            var restante = estado.MinutosJogados > 0
                ? Math.Max(0.0, total - estado.MinutosJogados)
                : total * pesoPendente / pesoTotal;

            DistribuirTempos(pendentes, restante, cfg, rng);
            foreach (var c in pendentes) c.Minuto = Math.Round(c.Minuto + estado.MinutosJogados, 1);
            // Com a duração toda gasta, os espaços sorteados passam do total
            estado.DuracaoMinutos = Math.Max(estado.MinutosJogados + restante, pendentes.Max(c => c.Minuto));
        }

        private static double Dificuldade(double porcentagem) =>
            Math.Log(100.0 / Math.Min(100.0, Math.Max(0.01, porcentagem)));

        // Peso mínimo para que conquistas de quase 100% não fiquem com espera zero
        private static double Peso(double dificuldade, Config cfg) => Math.Pow(Math.Max(0.05, dificuldade), cfg.Curva);

        // Normal padrão (Box-Muller)
        private static double Gaussiano(Random rng) =>
            Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2.0 * Math.PI * rng.NextDouble());
    }
}
