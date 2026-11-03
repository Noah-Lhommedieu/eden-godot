namespace EdenClient.Data.Tables;

/// <summary>
/// Tables de races du client :
///
/// - <see cref="Load"/> : <c>data/db/C_Classes.ini</c> — catégories
///   affichables (comma <c>V.2,5,</c>, 7 entrées : 0=débutant +
///   1..6 = classes de départ Big5). Historique — conservé tel quel.
/// - <see cref="LoadRaces"/> : <c>data/db/C_Races.ini</c> — les races
///   proposables à la création (comma <c>V.1,4,</c> : id, nom, param).
///   <b>Le fichier livré ne contient qu'une race</b> (id 1 人/humain,
///   identique projet et Reforged) — l'UI native n'offre donc que
///   l'humain. L'extension 5 races de
///   <see cref="CharCreateBodySpec"/> est une déviation assumée.
///
/// Spécification serveur : <c>ProgressionParsers.ParseRaces</c>
/// ($EDEN, S_Races.ini) et validation native
/// <c>world/CreateCharacterIntoDB @0x080a2170</c>.
/// </summary>
public static class RaceTable
{
    /// <summary>Une entrée de C_Classes.ini.</summary>
    public sealed class RaceDef
    {
        /// <summary>Col 0 — id de la catégorie.</summary>
        public int Id { get; init; }
        /// <summary>Col 1 — nom affichable (Big5 décodé).</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Col 2 — flag (1 sur toutes les lignes réelles).</summary>
        public int Flag { get; init; }
        /// <summary>Col 3 — paramètre (60 partout — HYPOTHESIS : borne
        /// de niveau ou de classe affichée ; rôle non prouvé).</summary>
        public int Param { get; init; }
    }

    /// <summary>Une entrée de C_Races.ini (V.1,4,) : id, nom Big5,
    /// param (12 sur la seule ligne livrée — rôle non prouvé).</summary>
    public sealed class RaceEntry
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Param { get; init; }
    }

    /// <summary>Charge C_Classes.ini → id → entrée.</summary>
    public static Dictionary<int, RaceDef> Load(string path)
    {
        var table = IniTable.ParseFile(path);
        var dict = new Dictionary<int, RaceDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 2 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new RaceDef
            {
                Id = id,
                Name = IniTable.GetStr(row, 1),
                Flag = IniTable.GetInt(row, 2),
                Param = IniTable.GetInt(row, 3),
            };
        }
        return dict;
    }

    /// <summary>Charge C_Races.ini → id → entrée (1 ligne livrée :
    /// l'humain). Retourne un dict vide si le fichier est absent.</summary>
    public static Dictionary<int, RaceEntry> LoadRaces(string path)
    {
        var table = IniTable.ParseFile(path);
        var dict = new Dictionary<int, RaceEntry>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 2 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new RaceEntry
            {
                Id = id,
                Name = IniTable.GetStr(row, 1),
                Param = IniTable.GetInt(row, 2),
            };
        }
        return dict;
    }
}

/// <summary>
/// Spec corps↔race — preuve native <c>CreateCharacterIntoDB</c>
/// (world @0x080a2170) + switch <c>rf_game FUN_0073ddc0</c>
/// (0→g,1→b,2→m,4→f,6→k,8→l,9→c) + ordre des sections de
/// <c>4pequip.ini</c> [TableGirl,Boy,Mouse,Frog,Kuma,Loli,Shota] :
///   race 1 Human   → codes 0 (g, ♀) / 1 (b, ♂)
///   race 2 Zumi    → code 2 (m)          — corps unique
///   race 3 Anuran  → code 4 (f)          — corps unique
///   race 4 Ursun   → code 6 (k)          — corps unique
///   race 5 Halfkin → codes 8 (l, ♀) / 9 (c, ♂)
/// Les codes impairs 3/5/7 sont <b>rejetés</b> par la validation
/// native (masque 0x317|0x40) → races 2-4 sans variante de genre.
/// Le byte « race » wire = index 1..5 (page de classe native =
/// (code&gt;&gt;1)×100 + classId).
/// </summary>
public static class CharCreateBodySpec
{
    /// <summary>Une race proposable à la création.</summary>
    public readonly record struct RaceSpec(int WireId, string Name,
        int FemaleBodyCode, int MaleBodyCode, bool Gendered);

    /// <summary>Les 5 races du wire (ids 1-5). Pour les races à corps
    /// unique les deux codes sont identiques (le choix genre est
    /// ignoré côté serveur).</summary>
    public static readonly RaceSpec[] Races =
    {
        new(1, "Human",   0, 1, true),
        new(2, "Zumi",    2, 2, false),
        new(3, "Anuran",  4, 4, false),
        new(4, "Ursun",   6, 6, false),
        new(5, "Halfkin", 8, 9, true),
    };

    /// <summary>Code corps pour (race, genre masculin?) — les races à
    /// corps unique ignorent le genre (les codes 3/5/7 sont refusés
    /// par le serveur natif).</summary>
    public static int BodyCode(int raceWireId, bool male)
    {
        foreach (var r in Races)
            if (r.WireId == raceWireId)
                return male && r.Gendered ? r.MaleBodyCode
                                          : r.FemaleBodyCode;
        return male ? 1 : 0;   // fallback humain
    }

    /// <summary>Bornes cosmétiques natives par code corps —
    /// <c>CreateCharacterIntoDB</c> : codes {0,1,2,4,8,9} → hair&lt;11,
    /// face&lt;5, hairColor&lt;15, skin&lt;10, eyes&lt;15 ;
    /// code 6 (Ursun) → hair&lt;8, face&lt;2 (face&lt;1 si skin==10),
    /// hairColor&lt;10, skin&lt;11. Valeurs = max inclus.</summary>
    public readonly record struct Bounds(int HairMax, int FaceMax,
        int HairColorMax, int SkinMax, int EyesMax);

    /// <summary>Bornes inclusives pour un code corps.</summary>
    public static Bounds BoundsFor(int bodyCode) => bodyCode == 6
        ? new Bounds(7, 1, 9, 10, 14)
        : new Bounds(10, 4, 14, 9, 14);
}
