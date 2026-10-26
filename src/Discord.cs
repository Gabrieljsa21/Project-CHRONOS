using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Chronos
{
    // Mostra "Jogando <jogo>" no Discord pelo Rich Presence (pipe local discord-ipc-N), com o ID
    // oficial do jogo na lista de jogos detectáveis do Discord. Não depende do nome do
    // executável, que nem todo jogo tem cadastrado (o 007 First Light não tem). O Discord limpa
    // a atividade sozinho quando o processo da sessão termina.
    internal sealed class Discord : IDisposable
    {
        private const string UrlDetectaveis = "https://discord.com/api/v9/applications/detectable";
        private static readonly TimeSpan ValidadeLista = TimeSpan.FromDays(1);

        private static string CaminhoMapa => Path.Combine(Caminhos.Dados, "discord_jogos.json");

        private readonly string idAplicacao;
        private readonly long inicio = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private NamedPipeClientStream pipe;

        private Discord(string idAplicacao)
        {
            this.idAplicacao = idAplicacao;
        }

        // null se o jogo não está na lista do Discord ou a lista não pôde ser baixada
        public static Discord Criar(uint appid)
        {
            try
            {
                return MapaSteamParaDiscord().TryGetValue(appid.ToString(), out var id) ? new Discord(id) : null;
            }
            catch (Exception e)
            {
                Log.Escrever($"lista de jogos do Discord indisponível ({e.Message})");
                return null;
            }
        }

        // Chamado a cada ciclo: conecta (ou reconecta, se o Discord foi aberto ou reiniciado
        // depois) e anuncia a atividade. Sem o Discord aberto, só tenta de novo no próximo ciclo.
        public void Manter()
        {
            if (pipe != null && pipe.IsConnected) return;
            Fechar();
            for (var i = 0; i < 10; i++)
            {
                var tentativa = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut);
                try
                {
                    tentativa.Connect(500);
                    pipe = tentativa;
                    Enviar(0, new { v = 1, client_id = idAplicacao });
                    Receber();
                    Enviar(1, new
                    {
                        cmd = "SET_ACTIVITY",
                        nonce = Guid.NewGuid().ToString(),
                        args = new { pid = System.Diagnostics.Process.GetCurrentProcess().Id, activity = new { timestamps = new { start = inicio } } },
                    });
                    Receber();
                    Log.Escrever("atividade anunciada no Discord");
                    return;
                }
                catch (Exception e) when (e is IOException || e is TimeoutException || e is InvalidOperationException)
                {
                    tentativa.Dispose();
                    pipe = null;
                }
            }
        }

        private void Enviar(int opcode, object dados)
        {
            var corpo = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dados));
            var quadro = new byte[8 + corpo.Length];
            BitConverter.GetBytes(opcode).CopyTo(quadro, 0);
            BitConverter.GetBytes(corpo.Length).CopyTo(quadro, 4);
            corpo.CopyTo(quadro, 8);
            pipe.Write(quadro, 0, quadro.Length);
            pipe.Flush();
        }

        private void Receber()
        {
            var cabecalho = LerExato(8);
            var opcode = BitConverter.ToInt32(cabecalho, 0);
            var corpo = Encoding.UTF8.GetString(LerExato(BitConverter.ToInt32(cabecalho, 4)));
            // 2 = CLOSE (ID recusado); resposta com "evt": "ERROR" = comando recusado
            if (opcode == 2 || corpo.Contains("\"evt\":\"ERROR\""))
            {
                throw new InvalidOperationException($"Discord recusou: {corpo}");
            }
        }

        private byte[] LerExato(int tamanho)
        {
            var dados = new byte[tamanho];
            var lidos = 0;
            while (lidos < tamanho)
            {
                var n = pipe.Read(dados, lidos, tamanho - lidos);
                if (n == 0) throw new IOException("pipe do Discord fechado");
                lidos += n;
            }
            return dados;
        }

        private void Fechar()
        {
            pipe?.Dispose();
            pipe = null;
        }

        public void Dispose() => Fechar();

        // {appid da Steam: ID do jogo no Discord}, refeito uma vez por dia a partir da lista
        // de detectáveis (13 MB), que não fica guardada inteira
        private static Dictionary<string, string> MapaSteamParaDiscord()
        {
            var fresco = File.Exists(CaminhoMapa) && DateTime.Now - File.GetLastWriteTime(CaminhoMapa) < ValidadeLista;
            if (!fresco)
            {
                try
                {
                    string texto;
                    using (var web = new WebClient { Encoding = Encoding.UTF8 })
                    {
                        texto = web.DownloadString(UrlDetectaveis);
                    }
                    var mapa = new Dictionary<string, string>();
                    using (var doc = JsonDocument.Parse(texto))
                    {
                        foreach (var app in doc.RootElement.EnumerateArray())
                        {
                            if (!app.TryGetProperty("third_party_skus", out var skus) || skus.ValueKind != JsonValueKind.Array) continue;
                            foreach (var sku in skus.EnumerateArray())
                            {
                                if (sku.TryGetProperty("distributor", out var d) && d.GetString() == "steam"
                                    && sku.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                                {
                                    mapa[id.GetString()] = app.GetProperty("id").GetString();
                                }
                            }
                        }
                    }
                    // Duas sessões podem refazer o mapa ao mesmo tempo: arquivo temporário próprio
                    var temporario = $"{CaminhoMapa}.{System.Diagnostics.Process.GetCurrentProcess().Id}.tmp";
                    File.WriteAllText(temporario, JsonSerializer.Serialize(mapa), new UTF8Encoding(false));
                    File.Copy(temporario, CaminhoMapa, true);
                    File.Delete(temporario);
                    return mapa;
                }
                catch (WebException) when (File.Exists(CaminhoMapa))
                {
                    // Sem internet: o mapa antigo serve
                }
            }
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CaminhoMapa));
        }
    }
}
