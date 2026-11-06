using System.IO;
using System.Text;
using System.Threading;

namespace Chronos
{
    internal static class Arquivo
    {
        // Grava num temporário, força a escrita no disco e só então troca pelo arquivo final.
        // Sem o Flush(true), uma queda de energia logo depois da troca deixava o arquivo
        // com o tamanho certo e o conteúdo zerado (06/11/2026, config.json cheio de 0x00).
        // A troca falha enquanto a GAIA ou outro processo do CHRONOS lê o arquivo: tenta de novo
        public static void GravarAtomico(string caminho, string conteudo, string temporario = null)
        {
            temporario = temporario ?? caminho + ".tmp";
            var bytes = new UTF8Encoding(false).GetBytes(conteudo);
            using (var fluxo = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fluxo.Write(bytes, 0, bytes.Length);
                fluxo.Flush(true);
            }
            for (var tentativa = 0; ; tentativa++)
            {
                try
                {
                    if (File.Exists(caminho)) File.Replace(temporario, caminho, null);
                    else File.Move(temporario, caminho);
                    return;
                }
                catch (IOException) when (tentativa < 10)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
