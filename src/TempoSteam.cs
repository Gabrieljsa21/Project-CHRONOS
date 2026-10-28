using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace Chronos
{
    // Lê as horas que a Steam registrou para o jogo na conta logada. O cliente guarda em
    // userdata/<conta>/config/localconfig.vdf (KeyValues em texto), no caminho
    // UserLocalConfigStore/Software/Valve/Steam/apps/<appid>/Playtime, em minutos.
    internal static class TempoSteam
    {
        // null quando o arquivo não existe ou o jogo nunca foi aberto na conta
        public static double? Minutos(uint appid)
        {
            var arquivo = LocalConfig();
            if (arquivo == null) return null;

            var caminho = new List<string>();
            string chave = null;
            foreach (var token in Tokens(File.ReadAllText(arquivo, Encoding.UTF8)))
            {
                if (token == "{")
                {
                    caminho.Add(chave ?? "");
                    chave = null;
                }
                else if (token == "}")
                {
                    if (caminho.Count > 0) caminho.RemoveAt(caminho.Count - 1);
                    chave = null;
                }
                else if (chave == null)
                {
                    chave = token;
                }
                else
                {
                    if (string.Equals(chave, "Playtime", StringComparison.OrdinalIgnoreCase)
                        && caminho.Count >= 2
                        && caminho[caminho.Count - 1] == appid.ToString()
                        && string.Equals(caminho[caminho.Count - 2], "apps", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(token, out var minutos))
                    {
                        return minutos;
                    }
                    chave = null;
                }
            }
            return null;
        }

        // A conta logada vem do registro; com a Steam fechada, usa o localconfig gravado por último
        private static string LocalConfig()
        {
            var userdata = Path.Combine(EsquemaSteam.PastaSteam() ?? "", "userdata");
            if (!Directory.Exists(userdata)) return null;

            using (var chave = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
            {
                var conta = Convert.ToUInt32(chave?.GetValue("ActiveUser") ?? 0);
                var ativo = Path.Combine(userdata, conta.ToString(), "config", "localconfig.vdf");
                if (conta != 0 && File.Exists(ativo)) return ativo;
            }
            return Directory.GetDirectories(userdata)
                .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        // Strings entre aspas (com \" e \\ escapados), "{" e "}"; o resto é ignorado
        private static IEnumerable<string> Tokens(string texto)
        {
            var i = 0;
            while (i < texto.Length)
            {
                var c = texto[i];
                if (c == '{' || c == '}')
                {
                    i++;
                    yield return c.ToString();
                }
                else if (c == '"')
                {
                    var valor = new StringBuilder();
                    i++;
                    while (i < texto.Length && texto[i] != '"')
                    {
                        if (texto[i] == '\\' && i + 1 < texto.Length) i++;
                        valor.Append(texto[i++]);
                    }
                    i++;
                    yield return valor.ToString();
                }
                else
                {
                    i++;
                }
            }
        }
    }
}
