namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/c_monster.ini</c> — base de monstres du client (~15k
/// entrées, <c>|V.6|62|</c> — même layout que S_Monster.ini serveur).
///
/// Sous-ensemble porté pour le client : code modèle (résolution
/// NIF/KF), nom, niveau, type (normal/élite/boss pour l'affichage du
/// cadre), HP/MP (barres), stats de combat visibles, rayon d'aggro.
/// Spécification serveur : <c>MonsterParser</c>
/// (<c>$EDEN/server/.../Parsers/MonsterParser.cs</c>) — indices prouvés
/// par <c>CMonsterQuery::Deserialize</c> @0x860d790/0x860da50 +
/// conversion CMonsterData→CMonsterTemplate (zone désasm).
///
/// Noms anglais : croiser <see cref="TextTable"/> sur t_monster.ini.
/// </summary>
public static class MonsterTable
{
    /// <summary>Un monstre depuis c_monster.ini.</summary>
    public sealed class MonsterDef
    {
        /// <summary>Col 0 — id monstre (20001…).</summary>
        public int Id { get; init; }
        /// <summary>Col 1 — code modèle (M312040) — résout le NIF/KFM
        /// du modèle via la table des modèles.</summary>
        public string ModelCode { get; init; } = string.Empty;
        /// <summary>Col 2 — nom Big5.</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Col 3 — famille/type (1, 4…) — aussi catégorie DSL
        /// KillMonster côté serveur.</summary>
        public int Family { get; init; }
        /// <summary>Col 4 — niveau.</summary>
        public int Level { get; init; }
        /// <summary>Col 5 — type de monstre : 1 normal, 2 élite/mini-boss,
        /// 3 boss, 4-5 spéciaux (cadre d'affichage client).</summary>
        public int MonsterType { get; init; }
        /// <summary>Élite ou boss — type ≥ 2.</summary>
        public bool IsEliteOrBoss => MonsterType >= 2;
        /// <summary>Col idx 10 — masque de faiblesses Break Point (9 bits,
        /// bit attr−1 : Slash, Strike, Pierce, Fire, Ice, Lightning,
        /// Nature, Light, Dark — <c>CMonsterData+0x28</c>).</summary>
        public int BpWeakMask { get; init; }
        /// <summary>Col idx 12 — patience max (jauge BP), défaut 100 si
        /// masque et patience valent 0 (<c>CMonsterQuery::Deserialize</c>
        /// @0x0860d790), cap 9 999 999.</summary>
        public int Patience { get; init; } = 100;
        /// <summary>Le mob peut être brisé : au moins une faiblesse et
        /// une patience positive.</summary>
        public bool CanBreak => BpWeakMask != 0 && Patience > 0;
        /// <summary>Col 7 — flag actif (1).</summary>
        public bool Enabled { get; init; }
        /// <summary>Col 8 — HP max (barre de vie / jauge boss).</summary>
        public long HP { get; init; }
        /// <summary>Col 9 — MP max.</summary>
        public long MP { get; init; }
        /// <summary>Col 13 — type/élément d'attaque (indexe le bloc
        /// résistances du défenseur).</summary>
        public int AttackElement { get; init; }
        /// <summary>Col 14 — puissance d'attaque physique.</summary>
        public int AttackPower { get; init; }
        /// <summary>Col 16 — portée d'attaque max.</summary>
        public int AttackRangeMax { get; init; }
        /// <summary>Col 17 — vitesse d'attaque.</summary>
        public int AttackSpeed { get; init; }
        /// <summary>Col 18 — défense physique.</summary>
        public int PhysicalDef { get; init; }
        /// <summary>Cols 25-33 — résistances élémentaires (9 valeurs,
        /// col 25 = physique).</summary>
        public int[] Resistances { get; init; } = new int[9];
        /// <summary>Col 37 — rayon d'aggro en cellules (×0.5 unité monde
        /// — conversion template native @0x8367a5).</summary>
        public int AggroCells { get; init; }
        /// <summary>Rayon d'aggro en unités monde (AggroCells × 0.5).</summary>
        public float AggroRadius => AggroCells * 0.5f;
        /// <summary>Col 38 — profil c_ai du monstre.</summary>
        public int AiId { get; init; }
        /// <summary>Col 53 — EXP accordée à la mort (preuve AttribLog
        /// natif : 56001 → 25).</summary>
        public long ExpReward { get; init; }
        /// <summary>Col 54 — exp de classe (NP) à la mort.</summary>
        public long ClassExp { get; init; }
        /// <summary>Mob immobile (coffre, autel, statue) — dérivé des
        /// données : ni attaque ni portée (même heuristique que le
        /// parser serveur — les mobs-objets n'ont pas d'état FSM de
        /// déplacement).</summary>
        public bool IsStationary => AttackPower <= 0 && AttackRangeMax <= 0;
        /// <summary>Cellules brutes de la ligne.</summary>
        public string[] Raw { get; init; } = Array.Empty<string>();
    }

    /// <summary>Charge c_monster.ini → id → définition.</summary>
    public static Dictionary<int, MonsterDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, MonsterDef>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            // Les fichiers natifs contiennent des stubs « id|modèle|nom »
            // (3 cellules — mobs simples, même forme dans S_Monster
            // serveur) : le seuil <20 les rendait introuvables alors
            // que le ModelCode sert au rendu.
            if (row.Length < 3 || !int.TryParse(row[0], out var id))
                continue;

            var m = new MonsterDef
            {
                Id = id,
                ModelCode = IniTable.GetStr(row, 1),
                Name = IniTable.GetStr(row, 2),
                Family = IniTable.GetInt(row, 3),
                Level = IniTable.GetInt(row, 4),
                MonsterType = IniTable.GetInt(row, 5),
                BpWeakMask = IniTable.GetInt(row, 10),
                Patience = BreakPointUi.ParsePatience(row),
                Enabled = IniTable.GetInt(row, 7) != 0,
                HP = long.TryParse(IniTable.GetStr(row, 8), out var hp) ? hp : 0,
                MP = long.TryParse(IniTable.GetStr(row, 9), out var mp) ? mp : 0,
                AttackElement = IniTable.GetInt(row, 13),
                AttackPower = IniTable.GetInt(row, 14),
                AttackRangeMax = IniTable.GetInt(row, 16),
                AttackSpeed = IniTable.GetInt(row, 17),
                PhysicalDef = IniTable.GetInt(row, 18),
                AggroCells = IniTable.GetInt(row, 37),
                AiId = IniTable.GetInt(row, 38),
                ExpReward = IniTable.GetLong(row, 53),
                ClassExp = IniTable.GetLong(row, 54),
                Raw = row,
            };

            for (var i = 0; i < 9; i++)
                m.Resistances[i] = IniTable.GetInt(row, 25 + i);

            dict[id] = m;
        }
        return dict;
    }
}
