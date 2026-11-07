using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Limitless.Studio.Services;

/// <summary>Tables de traduction EN du client Reforged —
/// `data/db/T_*.ini` (`id|Name|…`). Les records sont multi-lignes :
/// un record commence par `id|…` et se termine sur une ligne
/// finissant par `|` (les descriptions contiennent des sauts de
/// ligne). Le fichier s'ouvre par 2 lignes d'en-tête (nombre de
/// champs, puis les index de colonnes texte). La résolution de nom
/// est insensible à la casse (t_item.ini, T_Spell.ini…) et la
/// lecture est Latin-1 (fichiers client non-UTF8).</summary>
public sealed class TranslationCatalog
{
    private readonly string _dbDir;
    private readonly Dictionary<string, Dictionary<int, string>> _tables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _resolved =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tables où le nom d'affichage n'est PAS le 1er champ
    /// texte du record — ex. t_npc.ini : id|title \n name|tag|.</summary>
    private static readonly Dictionary<string, int> NameFieldIndex =
        new(StringComparer.OrdinalIgnoreCase)
        { ["t_npc.ini"] = 1 };

    public TranslationCatalog(string dbDir) => _dbDir = dbDir;

    /// <summary>Nom anglais d'un item, ou null.</summary>
    public string? ItemName(int id) => NameFor("t_item.ini", id);

    /// <summary>Nom anglais d'un monstre, ou null.</summary>
    public string? MonsterName(int id) => NameFor("t_monster.ini", id);

    /// <summary>Nom anglais de l'id `id` dans la table de traduction
    /// `transFile` (ex. "T_Spell.ini"), ou null si absente/vide.
    /// Résolution de fichier insensible à la casse.</summary>
    public string? NameFor(string transFile, int id)
    {
        var path = Resolve(transFile);
        if (path == null) return null;
        var table = Table(path);
        return table.TryGetValue(id, out var n) ? n : null;
    }

    /// <summary>Nom EN associé à l'id `id` d'une table serveur —
    /// déduit le T_*.ini par convention (S_Xxx → t_xxx) et le
    /// résout sur disque. Null si aucun fichier de traduction.</summary>
    public string? NameForIniRow(string iniFileName, int id)
    {
        var transFile = IniSchemas.TransFileFor(iniFileName);
        return transFile == null ? null : NameFor(transFile, id);
    }

    /// <summary>Le fichier T_*.ini existe-t-il réellement sur disque
    /// (nom résolu, insensible à la casse) ?</summary>
    public string? ResolveTransFile(string transFile) =>
        Resolve(transFile) is { } p ? Path.GetFileName(p) : null;

    /// <summary>Chemin réel d'un fichier du dossier db client —
    /// insensible à la casse, null si absent.</summary>
    private string? Resolve(string fileName)
    {
        if (_resolved.TryGetValue(fileName, out var p)) return p;
        string? found = null;
        if (Directory.Exists(_dbDir))
        {
            var direct = Path.Combine(_dbDir, fileName);
            if (File.Exists(direct))
                found = direct;
            else
                foreach (var f in Directory.GetFiles(_dbDir, "*.ini"))
                    if (string.Equals(Path.GetFileName(f), fileName,
                            StringComparison.OrdinalIgnoreCase))
                    { found = f; break; }
        }
        _resolved[fileName] = found;
        return found;
    }

    /// <summary>Charge (et cache) une table T_*.ini — accumulateur
    /// multi-lignes : fin de record = ligne se terminant par '|'.
    /// Le nom retenu est le 1er champ texte (ou celui indiqué par
    /// <see cref="NameFieldIndex"/>).</summary>
    private Dictionary<int, string> Table(string path)
    {
        var key = Path.GetFileName(path);
        if (_tables.TryGetValue(key, out var t)) return t;
        t = new Dictionary<int, string>();
        var nameIdx = NameFieldIndex.TryGetValue(key, out var ni)
            ? ni : 0;
        var head = new Regex(@"^(\d+)\|");
        var buf = new StringBuilder();
        foreach (var line in File.ReadLines(path, Encoding.Latin1))
        {
            buf.Append(line).Append('\n');
            if (!line.TrimEnd().EndsWith('|')) continue;
            var record = buf.ToString();
            buf.Clear();
            var m = head.Match(record);
            if (!m.Success
                || !int.TryParse(m.Groups[1].Value, out var id))
                continue;
            // Champs texte = tokens séparés par '|' ou saut de ligne
            // (t_npc colle title\nname dans le 1er champ '|').
            var fields = Regex.Split(record[m.Length..], @"[|\r\n]+",
                RegexOptions.None);
            string? name = null;
            var text = fields.Where(f => f.Trim().Length > 0)
                .Select(f => f.Trim()).ToArray();
            // Les records « boss » sont en 2 lignes :
            // `58001|<Earth's Relic> \nJustin Allspore|` — le 1er
            // champ texte est le TITRE `<…>`, pas le nom. On prend le
            // premier champ non-titre à partir de nameIdx (bug S1 :
            // les world boss étaient introuvables par leur nom).
            for (var k = 0; k < text.Length; k++)
            {
                var idx = nameIdx + k;
                if (idx >= text.Length) break;
                if (!IsTitleToken(text[idx])) { name = text[idx]; break; }
            }
            name ??= text.Length > nameIdx ? text[nameIdx]
                : text.Length > 0 ? text[0] : null;
            if (name is { Length: > 0 } n) t[id] = n;
        }
        _tables[key] = t;
        return t;
    }

    /// <summary>Token « titre » des records multi-champs —
    /// `<Earth's Relic>`, `<Mining Mushroid King>` : affiché au-dessus
    /// du nom en jeu, jamais le nom lui-même.</summary>
    private static bool IsTitleToken(string s) =>
        s.StartsWith('<') && s.EndsWith('>') && s.Length > 2;
}
