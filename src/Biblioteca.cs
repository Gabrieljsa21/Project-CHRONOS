using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;

namespace Chronos
{
    // Lista os jogos que a conta pode abrir, incluindo a biblioteca compartilhada da família
    // (a Web API da Steam só devolve os jogos próprios). Mesmo método do SAM.Picker: pergunta
    // à Steam, appid por appid, se a conta tem acesso, a partir da lista de jogos do SAM. Com a
    // variável STEAM_WEBAPI_KEY, soma a lista de jogos da loja inteira: a do SAM não tem jogos
    // recentes (Arknights: Endfield ficou de fora), e jogo gratuito nunca aberto também não
    // aparece na Web API de jogos próprios.
    internal static class Biblioteca
    {
        private const string UrlListaSam = "https://gib.me/sam/games.xml";
        private const string UrlListaLoja = "https://api.steampowered.com/IStoreService/GetAppList/v1/";
        private static readonly TimeSpan ValidadeLista = TimeSpan.FromDays(1);

        private static string CaminhoLista => Path.Combine(Caminhos.Dados, "games.xml");
        private static string CaminhoListaLoja => Path.Combine(Caminhos.Dados, "jogos_loja.txt");

        public static int Executar()
        {
            List<uint> candidatos;
            try
            {
                candidatos = Candidatos();
            }
            catch (Exception e)
            {
                Console.WriteLine($"erro: lista de jogos do SAM indisponível ({e.Message})");
                return 1;
            }

            using (var cliente = new SAM.API.Client())
            {
                try
                {
                    cliente.Initialize(0);
                }
                catch (SAM.API.ClientInitializeException e)
                {
                    Console.WriteLine($"erro: falha ao conectar na Steam ({e.Failure}): {e.Message}");
                    return Sessao.FalhaSteam;
                }

                var jogos = candidatos
                    .Where(appid => cliente.SteamApps008.IsSubscribedApp(appid))
                    .Select(appid => new { Appid = appid, Nome = cliente.SteamApps001.GetAppData(appid, "name") })
                    .ToList();
                Console.WriteLine(JsonSerializer.Serialize(jogos, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            }
            return 0;
        }

        // Só os jogos "normais" (sem type): demo, mod e junk ficam de fora, como no SAM
        private static List<uint> Candidatos()
        {
            var fresca = File.Exists(CaminhoLista) && DateTime.Now - File.GetLastWriteTime(CaminhoLista) < ValidadeLista;
            if (!fresca)
            {
                try
                {
                    using (var web = new WebClient())
                    {
                        web.DownloadFile(UrlListaSam, CaminhoLista + ".tmp");
                    }
                    File.Copy(CaminhoLista + ".tmp", CaminhoLista, true);
                    File.Delete(CaminhoLista + ".tmp");
                }
                catch (WebException) when (File.Exists(CaminhoLista))
                {
                    // Sem internet: a lista antiga serve
                }
            }

            var sam = XDocument.Load(CaminhoLista).Root.Elements("game")
                .Where(g => g.Attribute("type") == null)
                .Select(g => uint.Parse(g.Value));
            return sam.Union(CandidatosLoja()).ToList();
        }

        // Jogos da loja (IStoreService/GetAppList, só jogos, ~200 mil); vazio sem a chave ou
        // sem internet e sem cópia guardada
        private static IEnumerable<uint> CandidatosLoja()
        {
            var chave = Environment.GetEnvironmentVariable("STEAM_WEBAPI_KEY");
            var fresca = File.Exists(CaminhoListaLoja) && DateTime.Now - File.GetLastWriteTime(CaminhoListaLoja) < ValidadeLista;
            if (!fresca && !string.IsNullOrEmpty(chave))
            {
                try
                {
                    var appids = new List<uint>();
                    using (var web = new WebClient())
                    {
                        uint ultimo = 0;
                        while (true)
                        {
                            var url = $"{UrlListaLoja}?key={chave}&include_games=1&max_results=50000&last_appid={ultimo}";
                            using (var doc = JsonDocument.Parse(web.DownloadString(url)))
                            {
                                var resposta = doc.RootElement.GetProperty("response");
                                if (resposta.TryGetProperty("apps", out var apps))
                                {
                                    appids.AddRange(apps.EnumerateArray().Select(a => a.GetProperty("appid").GetUInt32()));
                                }
                                if (!resposta.TryGetProperty("have_more_results", out var mais) || !mais.GetBoolean()) break;
                                ultimo = resposta.GetProperty("last_appid").GetUInt32();
                            }
                        }
                    }
                    File.WriteAllLines(CaminhoListaLoja, appids.Select(a => a.ToString()));
                }
                catch (Exception e) when (e is WebException || e is JsonException || e is KeyNotFoundException)
                {
                    // Sem internet: a lista antiga (se houver) serve
                }
            }
            return File.Exists(CaminhoListaLoja)
                ? File.ReadLines(CaminhoListaLoja).Select(uint.Parse)
                : Enumerable.Empty<uint>();
        }
    }
}
