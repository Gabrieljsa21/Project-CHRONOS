using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Chronos
{
    // Lê o schema de conquistas que o cliente da Steam guarda em appcache/stats
    // (KeyValues binário). Com os Achievement Sets (beta de 2026-10), o schema traz
    // "groups/<id>/dlcappid" e cada conquista de DLC tem "groupid": a Steam recusa
    // essas conquistas para quem não tem a DLC.
    internal static class EsquemaSteam
    {
        // apiName da conquista -> appid da DLC que ela exige. Vazio se o jogo não usa sets
        // ou se o schema não está no disco (a Steam o baixa ao abrir o jogo).
        public static Dictionary<string, uint> ConquistasDeDlc(uint appid)
        {
            var resultado = new Dictionary<string, uint>();
            var caminho = Path.Combine(PastaSteam() ?? "", "appcache", "stats", $"UserGameStatsSchema_{appid}.bin");
            if (!File.Exists(caminho)) return resultado;

            Dictionary<string, object> raiz;
            using (var leitor = new BinaryReader(File.OpenRead(caminho)))
            {
                raiz = LerNo(leitor);
            }
            if (raiz.Count == 0) return resultado;
            var jogo = PrimeiroFilho(raiz);

            var dlcPorGrupo = new Dictionary<string, uint>();
            foreach (var grupo in Filhos(jogo, "groups"))
            {
                if (grupo.Value is Dictionary<string, object> g && g.TryGetValue("dlcappid", out var dlc))
                {
                    dlcPorGrupo[grupo.Key] = Convert.ToUInt32(dlc);
                }
            }
            if (dlcPorGrupo.Count == 0) return resultado;

            foreach (var stat in Filhos(jogo, "stats"))
            {
                if (!(stat.Value is Dictionary<string, object> s)) continue;
                foreach (var bit in Filhos(s, "bits"))
                {
                    if (bit.Value is Dictionary<string, object> b
                        && b.TryGetValue("name", out var nome)
                        && b.TryGetValue("groupid", out var grupo)
                        && dlcPorGrupo.TryGetValue(Convert.ToString(grupo), out var dlc))
                    {
                        resultado[(string)nome] = dlc;
                    }
                }
            }
            return resultado;
        }

        private static string PastaSteam()
        {
            using (var chave = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                return chave?.GetValue("SteamPath") as string;
            }
        }

        private static Dictionary<string, object> PrimeiroFilho(Dictionary<string, object> no)
        {
            foreach (var valor in no.Values)
            {
                if (valor is Dictionary<string, object> filho) return filho;
            }
            return new Dictionary<string, object>();
        }

        private static IEnumerable<KeyValuePair<string, object>> Filhos(Dictionary<string, object> no, string chave) =>
            no.TryGetValue(chave, out var valor) && valor is Dictionary<string, object> filho
                ? filho
                : new Dictionary<string, object>();

        // Tipos do KeyValues binário da Valve: 0 nó, 1 texto, 2 int32, 3 float, 4 ponteiro,
        // 5 texto UTF-16, 6 cor, 7 uint64, 8/11 fim do nó, 10 int64
        private static Dictionary<string, object> LerNo(BinaryReader leitor)
        {
            var no = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (leitor.BaseStream.Position < leitor.BaseStream.Length)
            {
                var tipo = leitor.ReadByte();
                if (tipo == 8 || tipo == 11) break;
                var nome = LerTexto(leitor);
                switch (tipo)
                {
                    case 0: no[nome] = LerNo(leitor); break;
                    case 1: no[nome] = LerTexto(leitor); break;
                    case 2: case 4: case 6: no[nome] = leitor.ReadInt32(); break;
                    case 3: no[nome] = leitor.ReadSingle(); break;
                    case 5: no[nome] = LerTextoLargo(leitor); break;
                    case 7: no[nome] = leitor.ReadUInt64(); break;
                    case 10: no[nome] = leitor.ReadInt64(); break;
                    default: throw new InvalidDataException($"tipo {tipo} desconhecido no schema");
                }
            }
            return no;
        }

        private static string LerTexto(BinaryReader leitor)
        {
            var bytes = new List<byte>();
            for (var b = leitor.ReadByte(); b != 0; b = leitor.ReadByte()) bytes.Add(b);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static string LerTextoLargo(BinaryReader leitor)
        {
            var texto = new StringBuilder();
            for (var c = leitor.ReadUInt16(); c != 0; c = leitor.ReadUInt16()) texto.Append((char)c);
            return texto.ToString();
        }
    }
}
