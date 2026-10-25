using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Chronos
{
    internal static class Log
    {
        private const long TamanhoMaximo = 5 * 1024 * 1024;

        public static string Prefixo { get; set; } = "agendador";

        public static void Escrever(string mensagem)
        {
            var linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Prefixo} {Process.GetCurrentProcess().Id}] {mensagem}{Environment.NewLine}";
            // Vários processos (agendador + sessões) escrevem no mesmo arquivo
            for (var tentativa = 0; tentativa < 5; tentativa++)
            {
                try
                {
                    Rotacionar();
                    using (var fs = new FileStream(Caminhos.Log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        var bytes = Encoding.UTF8.GetBytes(linha);
                        fs.Write(bytes, 0, bytes.Length);
                    }
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private static void Rotacionar()
        {
            var info = new FileInfo(Caminhos.Log);
            if (info.Exists && info.Length > TamanhoMaximo)
            {
                var antigo = Caminhos.Log + ".1";
                File.Delete(antigo);
                File.Move(Caminhos.Log, antigo);
            }
        }
    }
}
