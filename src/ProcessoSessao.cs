using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Chronos
{
    // Cada sessão roda de uma cópia do Chronos.exe com o nome do jogo ("CHRONOS - <jogo>.exe",
    // com a mesma descrição), para aparecer separada no Gerenciador de Tarefas e poder ser
    // encerrada sozinha. O prefixo fica de propósito: o nome nunca coincide com o executável
    // real do jogo. A cópia fica na pasta do Chronos.exe, que tem as DLLs de que ela precisa.
    internal static class ProcessoSessao
    {
        public const string Prefixo = "CHRONOS - ";

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string arquivo, bool apagarExistentes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateResource(IntPtr atualizacao, IntPtr tipo, IntPtr nome, ushort idioma, byte[] dados, uint tamanho);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr atualizacao, bool descartar);

        // Devolve o executável da sessão; se a cópia falhar, o próprio Chronos.exe
        public static string Preparar(string origem, string nomeJogo)
        {
            var nome = Prefixo + Limpar(nomeJogo);
            var destino = Path.Combine(Path.GetDirectoryName(origem), nome + ".exe");
            try
            {
                // Recompilado depois da cópia: copia de novo
                if (!File.Exists(destino) || File.GetLastWriteTimeUtc(destino) < File.GetLastWriteTimeUtc(origem))
                {
                    File.Copy(origem, destino, true);
                    TrocarDescricao(destino, nome);
                }
                // Redirecionamentos de assembly (System.Text.Json etc.), procurados pelo nome do exe
                File.Copy(origem + ".config", destino + ".config", true);
                return destino;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Log.Escrever($"não foi possível criar '{nome}.exe' ({e.Message}); a sessão usa o Chronos.exe");
                return origem;
            }
        }

        private static string Limpar(string nome)
        {
            var invalidos = Path.GetInvalidFileNameChars().Concat(new[] { '™', '®', '©' }).ToArray();
            var limpo = new string(nome.Where(c => !invalidos.Contains(c)).ToArray()).Trim().TrimEnd('.');
            return limpo.Length > 60 ? limpo.Substring(0, 60).Trim() : limpo;
        }

        // A aba "Processos" do Gerenciador de Tarefas mostra a FileDescription do recurso de
        // versão, não o nome do arquivo: troca o recurso inteiro por um com o nome do jogo
        private static void TrocarDescricao(string exe, string descricao)
        {
            var versao = Assembly.GetExecutingAssembly().GetName().Version;
            var dados = RecursoVersao(versao, descricao, Path.GetFileName(exe));
            var atualizacao = BeginUpdateResource(exe, false);
            if (atualizacao == IntPtr.Zero) throw new IOException($"BeginUpdateResource falhou ({Marshal.GetLastWin32Error()})");
            // RT_VERSION = 16, recurso 1, idioma neutro (o mesmo que o compilador grava)
            if (!UpdateResource(atualizacao, (IntPtr)16, (IntPtr)1, 0, dados, (uint)dados.Length))
            {
                EndUpdateResource(atualizacao, true);
                throw new IOException($"UpdateResource falhou ({Marshal.GetLastWin32Error()})");
            }
            if (!EndUpdateResource(atualizacao, false)) throw new IOException($"EndUpdateResource falhou ({Marshal.GetLastWin32Error()})");
        }

        // VS_VERSIONINFO: https://learn.microsoft.com/windows/win32/menurc/vs-versioninfo
        private static byte[] RecursoVersao(Version versao, string descricao, string arquivo)
        {
            var texto = versao.ToString();
            var fixo = new MemoryStream();
            var w = new BinaryWriter(fixo);
            var ms = (uint)(versao.Major << 16 | versao.Minor);
            var ls = (uint)(versao.Build << 16 | Math.Max(0, versao.Revision));
            // assinatura, versão da estrutura, arquivo MS/LS, produto MS/LS, máscara, flags,
            // VOS__WINDOWS32, VFT_APP, subtipo, data MS/LS
            foreach (var valor in new uint[] { 0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 4, 1, 0, 0, 0 }) w.Write(valor);

            var tabela = Bloco("000004b0", 1, null, 0,
                Texto("FileDescription", descricao),
                Texto("FileVersion", texto),
                Texto("InternalName", arquivo),
                Texto("OriginalFilename", arquivo),
                Texto("ProductName", "Chronos"),
                Texto("ProductVersion", texto));
            var traducao = Bloco("Translation", 0, new byte[] { 0, 0, 0xB0, 0x04 }, 4);
            return Bloco("VS_VERSION_INFO", 0, fixo.ToArray(), 52,
                Bloco("StringFileInfo", 1, null, 0, tabela),
                Bloco("VarFileInfo", 1, null, 0, traducao));
        }

        private static byte[] Texto(string chave, string valor) =>
            Bloco(chave, 1, Encoding.Unicode.GetBytes(valor + "\0"), (ushort)(valor.Length + 1));

        // Cabeçalho (tamanho, tamanho do valor, tipo), chave UTF-16, valor e filhos, alinhados a 4 bytes
        private static byte[] Bloco(string chave, ushort tipo, byte[] valor, ushort tamanhoValor, params byte[][] filhos)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((ushort)0);
            w.Write(tamanhoValor);
            w.Write(tipo);
            w.Write(Encoding.Unicode.GetBytes(chave + "\0"));
            if (valor != null)
            {
                Alinhar(ms);
                w.Write(valor);
            }
            foreach (var filho in filhos)
            {
                Alinhar(ms);
                w.Write(filho);
            }
            var bytes = ms.ToArray();
            BitConverter.GetBytes((ushort)bytes.Length).CopyTo(bytes, 0);
            return bytes;
        }

        private static void Alinhar(MemoryStream ms)
        {
            while (ms.Length % 4 != 0) ms.WriteByte(0);
        }
    }
}
