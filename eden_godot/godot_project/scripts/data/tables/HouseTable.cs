using EdenClient.Vfs;

namespace EdenClient.Data.Tables;

/// <summary>
/// Tables du hall de famille pour le rendu 3D des bâtiments —
/// mêmes sources natives que le serveur (<c>GameDataService</c>) :
/// <list type="bullet">
/// <item><c>data/db/c_buliding.ini</c> : <c>tid|code|nom|type|level|
/// prev_tid|npc_item_tid|…</c> (S_Buliding natif).</item>
/// <item><c>data/db/4PHouse.ini</c> : <c>DB&lt;n&gt;=code,modèle,texture,
/// dx dy dfacing,…</c> — le modèle NIF est
/// <c>house/model/&lt;modèle&gt;.nif</c>, les triplets sont les offsets
/// de slots NPC du manoir (inutilisés pour le rendu du bâtiment, dont
/// la position est le record [position] de l'aire du loc).</item>
/// </list>
/// </summary>
public static class HouseTable
{
    /// <summary>Un template S_Buliding.</summary>
    public sealed class BuildingDef
    {
        public int Tid { get; init; }
        /// <summary>Col 1 — code maison (ex. <c>H00101</c>), clé de
        /// <see cref="GetHouse"/>.</summary>
        public string Code { get; init; } = "";
        /// <summary>Col 3 — 2 = maison de hall, 4-9 = production,
        /// 10 = commerce (types {1,2,10} chaînés par niveau).</summary>
        public int Type { get; init; }
        /// <summary>Col 4 — niveau de famille requis.</summary>
        public int Level { get; init; }
        /// <summary>Col 5 — template du niveau précédent (chaîne
        /// d'upgrade ; le lien « next » natif +0x30 se déduit par
        /// inversion).</summary>
        public int PrevTid { get; init; }
        /// <summary>Col 6 — item NPC attaché (housekeeper).</summary>
        public int NpcItemTid { get; init; }
    }

    /// <summary>Un modèle de maison 4PHouse.</summary>
    public sealed class HouseDef
    {
        public string Code { get; init; } = "";
        /// <summary>Col 1 — nom du modèle (<c>house/model/&lt;M&gt;.nif</c>).</summary>
        public string Model { get; init; } = "";
        /// <summary>Col 2 — texture principale
        /// (<c>house/texture/&lt;T&gt;.dds</c>).</summary>
        public string Texture { get; init; } = "";
    }

    private static Dictionary<int, BuildingDef>? _buildings;
    private static Dictionary<string, HouseDef>? _houses;
    private static bool _tried;

    /// <summary>Template de bâtiment par tid S_Buliding.</summary>
    public static BuildingDef? GetBuilding(int tid) =>
        Ensure().buildings.TryGetValue(tid, out var b) ? b : null;

    /// <summary>Modèle de maison par code (H00101…).</summary>
    public static HouseDef? GetHouse(string code) =>
        Ensure().houses.TryGetValue(code, out var h) ? h : null;

    /// <summary>Template de la maison de hall pour un node d'instance
    /// et un niveau de famille — même chaînage que le serveur :
    /// base 1 (S501) ou 51 (S502), puis on remonte les <c>next</c>
    /// (def dont PrevTid == courant) tant que
    /// <c>def.Level &lt;= guildLevel</c>.</summary>
    public static BuildingDef? ResolveHallBuilding(int nodeId,
        int guildLevel)
    {
        var (buildings, _) = Ensure();
        if (!buildings.TryGetValue(nodeId == 502 ? 51 : 1, out var cur))
            return null;
        while (buildings.Values.FirstOrDefault(
                   b => b.PrevTid == cur.Tid && b.Level <= guildLevel)
               is { } next)
            cur = next;
        return cur;
    }

    /// <summary>Purge le cache — usage test uniquement : un premier
    /// <see cref="Ensure"/> exécuté avec un résolveur incorrect
    /// figeait des dictionnaires vides (<c>_tried</c> posé même sans
    /// fichier trouvé).</summary>
    internal static void ResetForTests()
    {
        _buildings = null;
        _houses = null;
        _tried = false;
    }

    private static (Dictionary<int, BuildingDef> buildings,
        Dictionary<string, HouseDef> houses) Ensure()
    {
        if (_buildings != null || _tried)
            return (_buildings ??= new(), _houses ??= new());
        _tried = true;
        _buildings = new Dictionary<int, BuildingDef>();
        _houses = new Dictionary<string, HouseDef>(
            StringComparer.OrdinalIgnoreCase);

        var path = (TextResolver.ResolveFile ??
            StoreTable.ResolveDefault)("data/db/c_buliding.ini");
        if (path != null)
        {
            var table = IniTable.ParseFile(path, EdenEncoding.Big5);
            foreach (var row in table.Rows)
            {
                if (row.Length < 7 ||
                    !int.TryParse(row[0], out var tid))
                    continue;
                _buildings[tid] = new BuildingDef
                {
                    Tid = tid, Code = row[1].Trim(),
                    Type = IniTable.GetInt(row, 3),
                    Level = IniTable.GetInt(row, 4),
                    PrevTid = IniTable.GetInt(row, 5),
                    NpcItemTid = IniTable.GetInt(row, 6),
                };
            }
        }

        var housePath = (TextResolver.ResolveFile ??
            StoreTable.ResolveDefault)("data/db/4PHouse.ini");
        if (housePath != null)
        {
            // Format INI clé=valeur (pas un record |V.x|) — lecture
            // ligne à ligne comme le parseur serveur.
            foreach (var rawLine in EdenVfs.ReadAllLines(housePath,
                         EdenEncoding.Big5))
            {
                var eq = rawLine.IndexOf('=');
                if (eq < 0 || !rawLine.TrimStart().StartsWith("DB"))
                    continue;
                var cols = rawLine[(eq + 1)..].TrimEnd(',', '\r')
                    .Split(',');
                if (cols.Length < 3) continue;
                _houses[cols[0].Trim()] = new HouseDef
                {
                    Code = cols[0].Trim(), Model = cols[1].Trim(),
                    Texture = cols[2].Trim(),
                };
            }
        }
        return (_buildings, _houses);
    }
}
