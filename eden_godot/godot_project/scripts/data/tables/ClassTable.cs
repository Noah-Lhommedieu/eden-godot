namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/C_Class.ini</c> — définitions des classes par race, côté
/// client (noms/descriptions Big5, codes classe, % de stats, sorts,
/// certificats, progression).
///
/// Format : <c>|V.6|65|</c> — même layout que le S_Class.ini serveur.
/// 305 entrées = 61 classes × 5 races ; ids 1-61 (race 1), 101-161
/// (race 2), 201-261 (race 3), 301-361 (race 4), 401-461 (race 5).
///
/// Spécification serveur : <c>ClassStatsParser.ParseClassDefinitions</c>
/// (<c>$EDEN/server/.../Parsers/ClassStatsParser.cs</c>) — l'alignement
/// par ancrage est repris tel quel : les champs texte (description,
/// prérequis) contiennent des '|' et des CRLF qui décalent la ligne ;
/// le bloc sorts (19 cellules d'ids ≥50000) sert d'ancre fiable.
///
/// Textes anglais : à croiser avec <see cref="ClassTextTable"/>
/// (T_Class.ini) — même ids pour la race de base (1-61).
/// </summary>
public static class ClassTable
{
    /// <summary>Définition d'une classe (par race) depuis C_Class.ini.</summary>
    public sealed class ClassDef
    {
        /// <summary>Col 0 — id composite race×100+classe.</summary>
        public int Id { get; set; }
        /// <summary>Race porteuse : Id/100 (0=race1 … 4=race5).</summary>
        public int RaceIndex => Id / 100;
        /// <summary>Classe de base : Id%100 (1-61).</summary>
        public int BaseClassIndex => Id % 100;
        /// <summary>Col 1 — nom Big5 (ex. 戰士 ; l'anglais est dans T_Class).</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Col 2 — index de la classe de base / ordre d'affichage.</summary>
        public int DisplayIndex { get; set; }
        /// <summary>Col 3 — code classe (J009, J033…) — utilisé par le
        /// client pour les assets UI/modèles.</summary>
        public string ClassCode { get; set; } = string.Empty;
        /// <summary>Description Big5 (cols 4+ — voir remarque ci-dessous).</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Nom du trait (Big5).</summary>
        public string TraitName { get; set; } = string.Empty;
        /// <summary>Description du trait (Big5).</summary>
        public string TraitDescription { get; set; } = string.Empty;
        /// <summary>Prérequis textuels (Big5, multi-lignes).</summary>
        public string RequirementText { get; set; } = string.Empty;
        /// <summary>Pourcentages de stats (9 cellules avant les
        /// certificats — HP, MP, STR, INT, CON, AGI, WIS, LUK, divers ;
        /// même ordre que ClassStatsParser.StatPercents).</summary>
        public int[] StatPercents { get; set; } = new int[9];
        /// <summary>Ids d'items-certificats par palier (8 cellules avant
        /// le bloc sorts — CClassData+0x44 natif).</summary>
        public int[] CertificateIds { get; set; } = new int[8];
        /// <summary>Ids de sorts de classe (bloc ancre : 19 cellules
        /// d'ids S_Spell 50xxx — vecteurs CClassData +0x50/+0x5c).</summary>
        public int[] ClassSpellIds { get; set; } = Array.Empty<int>();
        /// <summary>Ids de talents AA (6 cellules après le bloc sorts —
        /// CClassData+0x68).</summary>
        public int[] TalentIds { get; set; } = new int[6];
        /// <summary>Archétype d'arme (cellule certsBase-10 —
        /// CClassData+0x28) ∈ {1,2,3}.</summary>
        public int Archetype { get; set; }
        /// <summary>Progression par palier (10 cellules après les
        /// talents : « level Axxxxx Axxxxx 0 »).</summary>
        public List<LevelEntry> LevelProgression { get; } = new();
        /// <summary>Cellules brutes de la ligne.</summary>
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    /// <summary>Un palier de la progression « level Acert1 Acert2 flag ».</summary>
    public sealed class LevelEntry
    {
        public int Level { get; set; }
        public string CertCode1 { get; set; } = string.Empty;
        public string CertCode2 { get; set; } = string.Empty;
        public int Flag { get; set; }
    }

    /// <summary>Charge C_Class.ini → id composite → définition.</summary>
    public static Dictionary<int, ClassDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, ClassDef>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (row.Length < 22 || !int.TryParse(row[0], out var id))
                continue;

            var def = new ClassDef
            {
                Id = id,
                Name = IniTable.GetStr(row, 1),
                DisplayIndex = IniTable.GetInt(row, 2),
                ClassCode = IniTable.GetStr(row, 3),
                Raw = row,
            };

            // Ancrage identique au serveur (ClassStatsParser) : le bloc
            // sorts = premier run de ≥4 cellules ≥50000 ; certificats =
            // 8 cellules avant, stats = 9 avant eux, talents = 6 après,
            // progression = 10 après les talents.
            var spellBase = FirstSpellBlockIndex(row);
            if (spellBase < 0) spellBase = 30;   // fallback canonique
            var certsBase = spellBase - 8;

            def.Archetype = IniTable.GetInt(row, certsBase - 10);

            for (var i = 0; i < 9; i++)
                def.StatPercents[i] = IniTable.GetInt(row, certsBase - 9 + i);
            for (var i = 0; i < 8; i++)
                def.CertificateIds[i] = IniTable.GetInt(row, certsBase + i);

            var spells = new List<int>();
            for (var i = spellBase;
                 i <= spellBase + 18 && i < row.Length; i++)
            {
                var v = IniTable.GetInt(row, i);
                if (v > 0) spells.Add(v);
            }
            def.ClassSpellIds = spells.ToArray();

            for (var i = 0; i < 6; i++)
                def.TalentIds[i] = IniTable.GetInt(row, spellBase + 19 + i);

            for (var i = spellBase + 25;
                 i <= spellBase + 34 && i < row.Length; i++)
            {
                var parts = row[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && int.TryParse(parts[0], out var lvl))
                {
                    def.LevelProgression.Add(new LevelEntry
                    {
                        Level = lvl,
                        CertCode1 = parts[1],
                        CertCode2 = parts[2],
                        Flag = parts.Length > 3 && int.TryParse(parts[3], out var f) ? f : 0,
                    });
                }
            }

            // Champs texte : région cells[4 .. certsBase-10) = cols 4-11
            // (desc, trait, traitDesc, req, flag, vide, flags, ?) dont
            // les 4 dernières sont numériques/vides → on les retire.
            // Les 4 champs texte restants peuvent être éclatés par des
            // '|' embarqués : on les réattribue depuis la fin (la
            // description, texte le plus long, absorbe l'excédent) —
            // heuristique documentée, le cas nominal ≥4 cellules tient.
            var textEnd = Math.Max(4, certsBase - 10 - 4);
            var text = row[4..textEnd];
            if (text.Length >= 4)
            {
                def.RequirementText = text[^1];
                def.TraitDescription = text[^2];
                def.TraitName = text[^3];
                def.Description = string.Join('|', text[..^3]);
            }
            else if (text.Length > 0)
            {
                def.Description = string.Join('|', text);
            }

            dict[id] = def;
        }

        return dict;
    }

    /// <summary>Premier index du bloc de sorts — premier run de ≥4
    /// cellules ≥50000 (même heuristique que le parser serveur :
    /// les spellmasters de classe sont tous ≥50000).</summary>
    private static int FirstSpellBlockIndex(string[] row)
    {
        var run = 0;
        for (var i = 0; i < row.Length; i++)
        {
            run = IniTable.GetInt(row, i) >= 50000 ? run + 1 : 0;
            if (run >= 4) return i - 3;
        }
        return -1;
    }
}
