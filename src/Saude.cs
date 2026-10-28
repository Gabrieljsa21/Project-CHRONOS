using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Chronos
{
    // saude.json: o agendador regrava a cada ciclo com o que parou de funcionar. A GAIA mostra
    // "!" no indicador quando a lista não está vazia ou quando o arquivo para de ser atualizado
    internal static class Saude
    {
        public static string Caminho => Path.Combine(Caminhos.Dados, "saude.json");

        public static void Gravar(IReadOnlyList<string> problemas)
        {
            var conteudo = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["atualizado_em"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["problemas"] = problemas,
            }, Config.Json);
            var temporario = Caminho + ".tmp";
            try
            {
                File.WriteAllText(temporario, conteudo, new UTF8Encoding(false));
                if (File.Exists(Caminho)) File.Replace(temporario, Caminho, null);
                else File.Move(temporario, Caminho);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // A GAIA está lendo agora: o próximo ciclo grava de novo
            }
        }
    }
}
