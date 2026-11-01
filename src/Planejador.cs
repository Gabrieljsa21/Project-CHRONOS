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

            var completo = true;
            var tempos = new Dictionary<string, double>();
            if (jogo?.DuracaoHoras == null) completo = Tempos(estado, medianaMinutos, tempos);
            var (minutos, fonte) = Duracao(estado, jogo, tempos, cfg);
            estado.DuracaoTotalMinutos = minutos;
            // Com alguma fonte fora do ar fica null, e a próxima sessão consulta de novo
            estado.FonteDuracao = completo ? fonte : null;
            estado.DuracaoMinutos = estado.DuracaoTotalMinutos;
            DistribuirTempos(estado.Conquistas, estado.DuracaoMinutos, cfg, rng);
            estado.EspacosAjustados = true;
            return estado;
        }

        // duracao_horas do config.json vence as fontes. Sem ela, vale sempre a maior: fonte baixa
        // demais (SAM na mediana do SteamHunters, poucos registros no HowLongToBeat) nunca encurta
        // o plano (Bitburner: 51 min no SteamHunters, 1.645 h no HowLongToBeat)
        private static (double Minutos, string Fonte) Duracao(EstadoJogo estado, JogoConfig jogo, Dictionary<string, double> tempos, Config cfg)
        {
            if (jogo?.DuracaoHoras != null) return (jogo.DuracaoHoras.Value * 60.0 * cfg.FatorDuracao, "config");

            estado.TemposFontes = tempos.ToDictionary(t => t.Key, t => Math.Round(t.Value / 60.0, 1));
            var ordem = tempos.OrderBy(t => t.Value).ToList();
            double minutos;
            string fonte;
            if (ordem.Count > 0) { minutos = ordem.Last().Value; fonte = ordem.Last().Key; }
            else { minutos = cfg.DuracaoPadraoHoras * 60.0; fonte = "padrao"; }
            var detalhe = tempos.Count == 0 ? "nenhuma fonte tem o jogo" : string.Join(", ", ordem.Select(t => $"{t.Key} {t.Value / 60:F1} h"));
            Log.Escrever($"duração de {estado.Nome}: {detalhe}; vale {minutos / 60:F1} h ({fonte})");
            return (minutos * cfg.FatorDuracao, fonte);
        }

        // Duração atual do jogo para um plano que já existe: refaz a conta quando o config.json
        // manda (duracao_horas), quando deixou de mandar e quando o plano ainda não consultou
        // todas as fontes (anteriores à 0.11.0 ou com alguma fora do ar na última tentativa) e
        // quando a duração saiu da mediana da 0.11.0
        internal static double DuracaoAtual(EstadoJogo estado, JogoConfig jogo, Config cfg)
        {
            if (jogo?.DuracaoHoras != null)
            {
                estado.FonteDuracao = "config";
                return Duracao(estado, jogo, null, cfg).Minutos;
            }
            if (estado.DuracaoTotalMinutos > 0 && estado.FonteDuracao != null && estado.FonteDuracao != "config"
                && estado.FonteDuracao != "mediana") return estado.DuracaoTotalMinutos;

            var atual = estado.DuracaoTotalMinutos > 0 ? estado.DuracaoTotalMinutos : estado.DuracaoMinutos;
            double? mediana = null;
            if (estado.Fonte == "steamhunters")
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
                    return atual;
                }
            }
            var tempos = new Dictionary<string, double>();
            if (!Tempos(estado, mediana, tempos)) return atual;

            var (minutos, fonte) = Duracao(estado, jogo, tempos, cfg);
            estado.FonteDuracao = fonte;
            return minutos;
        }

        // Tempo de 100% em minutos de cada fonte que tem o jogo. Falso quando o HowLongToBeat ou
        // o curador falharam (fonte fora do ar, ou plano feito só com a Steam, que ainda não
        // sabe o nome do jogo e o recebe da sessão pelo schema)
        private static bool Tempos(EstadoJogo estado, double? medianaSteamHunters, Dictionary<string, double> tempos)
        {
            if (medianaSteamHunters > 0) tempos["steamhunters"] = medianaSteamHunters.Value;
            var completo = true;
            if (estado.Nome == estado.Appid.ToString()) completo = false;
            else completo &= Consultar("HowLongToBeat", estado, () => HowLongToBeat.Minutos100(estado.Appid, estado.Nome), "howlongtobeat", tempos);
            completo &= Consultar("curador da Steam", estado, () => CuradorSteam.Minutos100(estado.Appid), "curador", tempos);
            return completo;
        }

        private static bool Consultar(string nome, EstadoJogo estado, Func<double?> consulta, string chave, Dictionary<string, double> tempos)
        {
            try
            {
                var minutos = consulta();
                if (minutos > 0) tempos[chave] = minutos.Value;
                return true;
            }
            catch (Exception e)
            {
                Log.Escrever($"{nome} indisponível para {estado.Appid} ({e.Message}); nova consulta na próxima sessão");
                return false;
            }
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

        // Conquistas do schema que o plano não conhece (o jogo ganhou depois). As que o
        // SteamHunters marca como impossíveis vão para Descartadas. A porcentagem vem do
        // SteamHunters ou, se ele ainda não tem a conquista, da Steam; sem nenhuma das duas, fica
        // a menor do plano. Lança exceção se o SteamHunters falhar num plano feito com ele, para
        // nenhuma impossível entrar: a sessão tenta de novo na próxima conferência.
        internal static List<Conquista> ConquistasNovas(EstadoJogo estado, List<string> doSchema)
        {
            var conhecidas = new HashSet<string>(estado.Conquistas.Select(c => c.ApiName).Concat(estado.Descartadas));
            var faltando = doSchema.Where(api => !conhecidas.Contains(api)).Distinct().ToList();
            var novas = new List<Conquista>();
            if (faltando.Count == 0) return novas;

            var doHunters = new Dictionary<string, JsonElement>();
            JsonDocument lista = null;
            if (estado.Fonte == "steamhunters")
            {
                lista = BaixarJson($"https://steamhunters.com/api/apps/{estado.Appid}/achievements");
                foreach (var item in lista.RootElement.EnumerateArray()) doHunters[item.GetProperty("apiName").GetString()] = item;
            }
            using (lista)
            {
                Dictionary<string, double> daSteam = null;
                foreach (var api in faltando)
                {
                    var conquista = new Conquista { ApiName = api, Nome = api };
                    if (doHunters.TryGetValue(api, out var item))
                    {
                        if (item.TryGetProperty("obtainability", out var obt) && obt.GetInt32() != 0)
                        {
                            estado.Descartadas.Add(api);
                            continue;
                        }
                        conquista.Nome = item.GetProperty("name").GetString();
                        conquista.Porcentagem = item.GetProperty("steamPercentage").GetDouble();
                    }
                    else
                    {
                        daSteam = daSteam ?? PorcentagensSteam(estado.Appid);
                        conquista.Porcentagem = daSteam.TryGetValue(api, out var p)
                            ? p
                            : estado.Conquistas.Select(c => c.Porcentagem).DefaultIfEmpty(10.0).Min();
                    }
                    novas.Add(conquista);
                }
            }
            return novas;
        }

        private static Dictionary<string, double> PorcentagensSteam(uint appid)
        {
            var resultado = new Dictionary<string, double>();
            try
            {
                var temp = new EstadoJogo { Appid = appid, Nome = "-" };
                CarregarSteam(temp);
                foreach (var c in temp.Conquistas) resultado[c.ApiName] = c.Porcentagem;
            }
            catch (Exception e)
            {
                Log.Escrever($"porcentagens da Steam indisponíveis para {appid} ({e.Message})");
            }
            return resultado;
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
            // Muitas conquistas para pouca duração (GooCubelets: 362 pendentes já fora das 7,3 h do
            // jogo dariam 136 h só de espaços): o intervalo encolhe para caber, sem passar do piso
            var media = (minimo + maximo) / 2.0;
            if (n * media > duracaoMinutos && minimo > 0)
            {
                var piso = Math.Max(0.0, Math.Min(cfg.IntervaloPisoMinutos, minimo));
                var fator = Math.Max(piso / minimo, duracaoMinutos / (n * media));
                minimo *= fator;
                maximo = Math.Max(maximo * fator, minimo * 2.0);
            }
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
        // já tinha as comuns não esperar por elas de novo. Com `fatiaMinima` (conquistas que
        // voltaram a pendentes: removidas da conta ou novas no jogo), a fatia de peso vale também
        // para quem já passou da duração total, para elas saírem com horas entre si.
        internal static void Replanejar(EstadoJogo estado, Config cfg, Random rng, bool fatiaMinima = false)
        {
            var pendentes = estado.Conquistas.Where(c => c.Situacao == Situacao.Pendente).ToList();
            if (pendentes.Count == 0) return;
            var total = estado.DuracaoTotalMinutos > 0 ? estado.DuracaoTotalMinutos : estado.DuracaoMinutos;
            var pesoTotal = estado.Conquistas.Sum(c => Peso(Dificuldade(c.Porcentagem), cfg));
            var pesoPendente = pendentes.Sum(c => Peso(Dificuldade(c.Porcentagem), cfg));
            var fatia = total * pesoPendente / pesoTotal;
            var restante = estado.MinutosJogados > 0
                ? Math.Max(fatiaMinima ? fatia : 0.0, total - estado.MinutosJogados)
                : fatia;

            DistribuirTempos(pendentes, restante, cfg, rng);
            foreach (var c in pendentes) c.Minuto = Math.Round(c.Minuto + estado.MinutosJogados, 1);
            estado.EspacosAjustados = true;
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
