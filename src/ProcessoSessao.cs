using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Chronos
{
    // Cada sessão roda de uma cópia do Chronos.exe com o nome do executável que a Steam abre
    // para o jogo ("My Sexy Neighbour 2.exe"), para aparecer igual ao jogo no Gerenciador de
    // Tarefas e poder ser encerrada sozinha. Dois jogos podem ter o mesmo executável
    // ("Game.exe"), então cada cópia fica em sessoes/<appid>/, com as DLLs e o .exe.config.
    internal static class ProcessoSessao
    {
        // Nome das cópias até a 0.4.0, na pasta do Chronos.exe
        private const string PrefixoAntigo = "CHRONOS - ";
        private const int MaximoOpcoesInicio = 20;

        // Caminho do executável relativo à pasta do jogo, por appid (null: a Steam não informou)
        private static readonly Dictionary<uint, string> Executaveis = new Dictionary<uint, string>();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string arquivo, bool apagarExistentes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateResource(IntPtr atualizacao, IntPtr tipo, IntPtr nome, ushort idioma, byte[] dados, uint tamanho);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr atualizacao, bool descartar);

        [DllImport("version.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFileVersionInfoSize(string arquivo, out uint ignorado);

        [DllImport("version.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetFileVersionInfo(string arquivo, uint ignorado, uint tamanho, byte[] dados);

        // Devolve o executável da sessão; se a cópia falhar, o próprio Chronos.exe
        public static string Preparar(string origem, uint appid, string nomeJogo)
        {
            var pastaOrigem = Path.GetDirectoryName(origem);
            ApagarCopiasAntigas(pastaOrigem);

            var relativo = ExecutavelDoJogo(appid);
            // Steam fechada: nome provisório, trocado pelo real numa próxima sessão
            var nome = relativo != null ? Path.GetFileName(relativo) : Limpar(nomeJogo) + ".exe";
            var pasta = Path.Combine(pastaOrigem, "sessoes", appid.ToString());
            var destino = Path.Combine(pasta, nome);
            try
            {
                Directory.CreateDirectory(pasta);
                // Executável de outro nome (o provisório, ou o jogo trocou de executável)
                foreach (var outro in Directory.GetFiles(pasta, "*.exe*"))
                {
                    if (!outro.StartsWith(destino, StringComparison.OrdinalIgnoreCase)) File.Delete(outro);
                }
                // Recompilado depois da cópia: copia de novo
                if (Desatualizado(origem, destino))
                {
                    File.Copy(origem, destino, true);
                    TrocarVersao(destino, VersaoDoJogo(appid, relativo));
                }
                foreach (var dll in Directory.GetFiles(pastaOrigem, "*.dll"))
                {
                    var alvo = Path.Combine(pasta, Path.GetFileName(dll));
                    if (Desatualizado(dll, alvo)) File.Copy(dll, alvo, true);
                }
                // Redirecionamentos de assembly (System.Text.Json etc.), procurados pelo nome do exe
                File.Copy(origem + ".config", destino + ".config", true);
                return destino;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Log.Escrever($"não foi possível criar '{nome}' ({e.Message}); a sessão usa o Chronos.exe");
                return origem;
            }
        }

        private static bool Desatualizado(string origem, string destino) =>
            !File.Exists(destino) || File.GetLastWriteTimeUtc(destino) < File.GetLastWriteTimeUtc(origem);

        private static void ApagarCopiasAntigas(string pasta)
        {
            foreach (var antigo in Directory.GetFiles(pasta, PrefixoAntigo + "*"))
            {
                try { File.Delete(antigo); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        // A primeira opção de início para Windows do appinfo (config/launch/N), a mesma que a
        // Steam usa ao clicar em Jogar; a Steam informa até para jogo não instalado
        private static string ExecutavelDoJogo(uint appid)
        {
            if (Executaveis.TryGetValue(appid, out var conhecido)) return conhecido;
            using (var cliente = new SAM.API.Client())
            {
                try
                {
                    cliente.Initialize(0);
                }
                catch (SAM.API.ClientInitializeException e)
                {
                    Log.Escrever($"executável de {appid} indisponível, falha ao conectar na Steam ({e.Failure})");
                    return null;
                }
                string escolhido = null;
                for (var i = 0; i < MaximoOpcoesInicio && escolhido == null; i++)
                {
                    var chave = $"config/launch/{i}/";
                    var exe = cliente.SteamApps001.GetAppData(appid, chave + "executable");
                    if (string.IsNullOrEmpty(exe)) break;
                    var sistemas = cliente.SteamApps001.GetAppData(appid, chave + "config/oslist") ?? "";
                    var tipo = cliente.SteamApps001.GetAppData(appid, chave + "type") ?? "";
                    // Opções de VR, servidor, editor etc. ficam de fora
                    if (sistemas != "" && !sistemas.Contains("windows")) continue;
                    if (tipo != "" && tipo != "default" && tipo != "none") continue;
                    exe = exe.Replace('/', '\\').TrimStart('\\');
                    var arquivo = Path.GetFileName(exe);
                    if (arquivo.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) escolhido = exe;
                }
                if (escolhido == null) Log.Escrever($"a Steam não informou o executável de {appid}; a sessão usa o nome do jogo");
                Executaveis[appid] = escolhido;
                return escolhido;
            }
        }

        // Recurso de versão do executável real, com o jogo instalado; null se não estiver
        // instalado ou se o executável não tiver recurso (a cópia fica sem, como o jogo)
        private static byte[] VersaoDoJogo(uint appid, string relativo)
        {
            var instalacao = PastaInstalacao(appid);
            if (relativo == null || instalacao == null) return null;
            var real = Path.Combine(instalacao, relativo);
            if (!File.Exists(real)) return null;
            var tamanho = GetFileVersionInfoSize(real, out _);
            if (tamanho == 0) return null;
            var dados = new byte[tamanho];
            if (!GetFileVersionInfo(real, 0, tamanho, dados)) return null;
            // A API devolve espaço extra depois do VS_VERSIONINFO; o tamanho dele está no cabeçalho
            return dados.Take(BitConverter.ToUInt16(dados, 0)).ToArray();
        }

        private static string PastaInstalacao(uint appid)
        {
            foreach (var biblioteca in PastasBiblioteca())
            {
                var manifesto = Path.Combine(biblioteca, "steamapps", $"appmanifest_{appid}.acf");
                if (!File.Exists(manifesto)) continue;
                var pasta = Valor(File.ReadAllText(manifesto), "installdir").FirstOrDefault();
                return pasta == null ? null : Path.Combine(biblioteca, "steamapps", "common", pasta);
            }
            return null;
        }

        private static IEnumerable<string> PastasBiblioteca()
        {
            var steam = EsquemaSteam.PastaSteam();
            if (steam == null) return Enumerable.Empty<string>();
            var lista = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            var pastas = File.Exists(lista) ? Valor(File.ReadAllText(lista), "path") : Enumerable.Empty<string>();
            return new[] { steam }.Concat(pastas).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // Valores de "chave" "valor" num arquivo VDF de texto, com as barras desescapadas
        private static IEnumerable<string> Valor(string vdf, string chave) =>
            Regex.Matches(vdf, $"\"{chave}\"\\s+\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

        private static string Limpar(string nome)
        {
            var invalidos = Path.GetInvalidFileNameChars().Concat(new[] { '™', '®', '©' }).ToArray();
            var limpo = new string(nome.Where(c => !invalidos.Contains(c)).ToArray()).Trim().TrimEnd('.');
            return limpo.Length > 60 ? limpo.Substring(0, 60).Trim() : limpo;
        }

        // A aba "Processos" do Gerenciador de Tarefas mostra a FileDescription do recurso de
        // versão, ou o nome do arquivo se não houver: a cópia fica com o recurso do jogo real,
        // ou sem nenhum (o do Chronos.exe diria "Chronos")
        private static void TrocarVersao(string exe, byte[] dados)
        {
            var atualizacao = BeginUpdateResource(exe, false);
            if (atualizacao == IntPtr.Zero) throw new IOException($"BeginUpdateResource falhou ({Marshal.GetLastWin32Error()})");
            // RT_VERSION = 16, recurso 1, idioma neutro (o mesmo que o compilador grava); sem dados, apaga
            if (!UpdateResource(atualizacao, (IntPtr)16, (IntPtr)1, 0, dados, (uint)(dados?.Length ?? 0)))
            {
                EndUpdateResource(atualizacao, true);
                throw new IOException($"UpdateResource falhou ({Marshal.GetLastWin32Error()})");
            }
            if (!EndUpdateResource(atualizacao, false)) throw new IOException($"EndUpdateResource falhou ({Marshal.GetLastWin32Error()})");
        }
    }
}
