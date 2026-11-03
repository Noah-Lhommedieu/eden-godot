using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Monster.ini — 15547 monstres (|V.6|62|).
///
/// Colonnes identifiees:
///   [0]  Monster ID (20001...)
///   [1]  Code modele (M312040)
///   [2]  Nom (Big5: 玫瑰騎士 = Rose Knight)
///   [3]  Famille/type (1, 4...)
///   [4]  Niveau (80)
///   [7]  Flag actif (1)
///   [8]  HP (202589 boss / 9000000 tourelles)
///   [9]  MP
///   [13] Type d'attaque (1 melee, 2, 4...)
///   [14] Id de motion d'attaque — DB id dans Motion.ini
///        (56001 -> 186 = m025,1021,'attack01' ; boss 20001 -> 53641 =
///        'attack04'). C'est la valeur que le serveur émet dans le
///        ClientEffectReport (292) pour l'animation de coup.
///   [15-17] Stats combat (attaque/defense/vitesse)
///   [18] Puissance d'attaque (13410)
///   [19-24] Stats secondaires (crit, esquive, precision...)
///   [25-33] Resistances elementaires (9 valeurs: ~67/45/100)
///   [34-38] Crit/vitesse/range (4, 14, 20, 24, 8)
///   [40-44] IDs de spells/drops (48207, 48187, 59302)
///   [59, 61] Flags
///
/// Reversed: CMonster, CMonsterDBO, CAIFSM*
/// Semantique exacte par colonne a confirmer via decompilation du loader.
/// </summary>
public static class MonsterParser
{
    public sealed class MonsterDef
    {
        public int Id { get; set; }
        public string ModelCode { get; set; } = string.Empty;  // M312040
        public string Name { get; set; } = string.Empty;       // Big5
        public int Family { get; set; }
        public int Level { get; set; }
        /// <summary>Type de monstre (col 6 1-idx) : 1 normal, 2 élite/
        /// mini-boss, 3 boss, 4-5 spéciaux — la famille `MonsterTypeCheck`
        /// native. Pilote le leash : sur map instance (NodeDef.Type≥4)
        /// les types ≥2 gardent le leash distance, les autres jamais.</summary>
        public int MonsterType { get; set; }
        /// <summary>Élite ou boss — type ≥ 2.</summary>
        public bool IsEliteOrBoss => MonsterType >= 2;
        /// <summary>Catégorie de kill DSL (col 4 1-idx →
        /// CMonsterData+0x10, 3ᵉ token du Deserialize
        /// CMonsterQuery @0x860d790). Valeurs réelles : 2, 4, 6, 7.
        /// Passée à <c>CEvaluate::GainKillScore</c> comme param_1
        /// (CharacterKillMonster @0x8083f40) : buckets {2}=mon,
        /// {3,4}=elite, {5,6}=boss — utilisée par les clauses
        /// <c>KillMonster &lt;cat&gt; &lt;n&gt;</c> de S_RewardScore.
        /// ≠ <see cref="MonsterType"/> (col 6 1-idx) qui est le type
        /// d'affichage/leash.</summary>
        public int KillType { get; set; }
        /// <summary>Score DoD par kill (col 61 1-idx →
        /// CMonsterData+0xc4, 61ᵉ token du Deserialize — lecture
        /// conditionnelle version&gt;4). Passée à
        /// <c>CEvaluate::GainKillScore</c> comme param_2 : 0 → kill
        /// ignoré (early return natif, compteur NON incrémenté) ;
        /// sinon += au score brut +0x44. Seuls les mobs DoD
        /// (`＜毀滅…＞`, `＜沼澤霸主＞`…) la renseignent (~349 lignes,
        /// valeur typique 100). ≠ <see cref="ExpReward"/> (col 54
        /// 1-idx). ⚠️ avant le lot13 on lisait col 50 (0-idx) =
        /// chance de summon (`data+0x98`) — corrigé sur preuve désasm
        /// `CMonsterQuery::Deserialize` @0x860d790 (token 60 → +0xc4).
        /// </summary>
        public long KillScore { get; set; }
        public bool Enabled { get; set; }
        public long HP { get; set; }
        public long MP { get; set; }
        public int AttackType { get; set; }
        /// <summary>Élément d'attaque (col 13 0-idx → data+0x34 →
        /// CMonsterTemplate+0x70) — indexe le bloc résistances du
        /// défenseur via vtable+0xe8 dans `CalculatePhysicalDamage`
        /// @0x83fdec0. '1' boss 20001, '2' mobs standards, 5 esprit
        /// de feu.</summary>
        public int AttackElement { get; set; }
        /// <summary>Puissance d'attaque physique (col 14 0-idx →
        /// data+0x38 → CMonsterTemplate+0x4c) — désasm
        /// `CalculatePhysicalDamage(CMonster)` @0x83fdec0 :
        /// `ROUND((100+buff%)×col14×0.01)+flat`. lv1 56001→186,
        /// boss 20001→53641.</summary>
        public int AttackPower { get; set; }
        /// <summary>Variance d'attaque en % (col 15 → data+0x3c →
        /// CMonsterTemplate+0x44) — multiplicateur
        /// `100 − var + rand[0,2var)` dans CalculatePhysicalDamage.
        /// lv1→10, boss→10.</summary>
        public int AttackVariance { get; set; }
        /// <summary>Portée d'attaque max (col 16 → data+0x3e →
        /// template+0x40) — `AttackRange(CMonster)` @0x83f3dd0 :
        /// `env + col16`.</summary>
        public int AttackRangeMax { get; set; }
        /// <summary>Vitesse d'attaque (col 17 → data+0x40 →
        /// template+0x38) — `AttackSpeed(CMonster)` @0x83f83b0 :
        /// `col17 × (100−buff) / 100`, plancher 12.</summary>
        public int AttackSpeed { get; set; }
        /// <summary>Base d'esquive (col 20 → data+0x4a →
        /// template+0x50) — `PhysicalDodgeRate(CMonster)`
        /// @0x83f6ff0.</summary>
        public int Dodge { get; set; }
        /// <summary>Base de précision (col 19 → data+0x48 →
        /// template+0x52) — `PhysicalHitRate(CMonster)`.</summary>
        public int Hit { get; set; }
        /// <summary>Crit physique (col 21 → data+0x4c →
        /// template+0x54) — `PhysicalCriticalRate(CMonster)`
        /// @0x83f8660.</summary>
        public int PhysicalCrit { get; set; }
        /// <summary>Dégâts crit phys % (col 22 → data+0x4e →
        /// template+0x58) — `PhysicalCriticalDamage(CMonster)`
        /// @0x83f4690 : `ROUND(para311) + buff + col22`, clamp [0,300].</summary>
        public int PhysicalCritDamage { get; set; }
        /// <summary>Crit magique (col 23) — pendant magique de
        /// PhysicalCrit.</summary>
        public int MagicCrit { get; set; }
        /// <summary>Dégâts crit mag % (col 24).</summary>
        public int MagicCritDamage { get; set; }
        /// <summary>Défense physique (col 18 → data+0x44 →
        /// template+0x5c) — `PhysicalDefence(CMonster)` @0x83f46e0 :
        /// `ROUND(col18×(100+buff%)×0.01)+flat`, clamp [0,99999].</summary>
        public int PhysicalDef { get; set; }
        /// <summary>Défense magique — `MagicDefence(CMonster)`
        /// @0x83f3d70 lit `template+0x60 × env + flat` ; la conversion
        /// `CMonsterData→CMonsterTemplate` n'écrit JAMAIS +0x60 →
        /// toujours 0 natif (le champ col37 n'est pas routé vers le
        /// template combat).</summary>
        public int MagicDef { get; set; }
        /// <summary>Résistance physique (col 25 → template+0xa8, 1er
        /// octet du bloc résistances) — lue par le vtable +0xe8 dans
        /// `CalculatePhysicalDamage` quand l'attaque est élémentaire.</summary>
        public int PhysicalResist => Resistances[0];
        /// <summary>Expérience accordée à la mort — S_Monster col 54
        /// (1-indexé). Preuve AttribLog natif (ZoneServer/Attrib1011) :
        /// 56001 → EXP Gain:25 (col54=25, PAS col19=47), 56211 → 36
        /// (col54=36).</summary>
        public long ExpReward { get; set; }
        /// <summary>Exp de classe (NP) accordée à la mort — S_Monster
        /// col 55 (1-indexé), lue par CMonster::GiveNp → GainClassPoint
        /// (preuve AttribLog natif : 56001 → NP Gain:5, col55=5 ;
        /// 56211 → 7, col55=7). ≠ ExpReward.</summary>
        public long ClassExp { get; set; }
        /// <summary>Id de motion d'attaque — historiquement lu col 14
        /// (= attaque réelle après correction du mapping) ; conservé
        /// pour compatibilité, la vraie source d'anims est le pool
        /// `LiveMob.AttackKfIds` (motion table du modèle).</summary>
        public int AttackMotionId { get; set; }
        public int[] CombatStats { get; set; } = new int[10];  // cols 15-24
        public int[] Resistances { get; set; } = new int[9];   // cols 25-33
        public int[] AuxStats { get; set; } = new int[5];      // cols 34-38
        /// <summary>Ids actifs du monstre (cols 40-49) — références
        /// brutes : sorts (S_Spell 50xxx-60xxx), enchants/procs
        /// (S_Enchant 10xxx-49xxx) et tables de drop de classe
        /// (S_ClassDropItem 56xxx-63xxx). Les plages se chevauchent
        /// (59277 existe dans S_Spell ET dans la plage class-drop) →
        /// le tri est fait au chargement par SpellIds/ClassDropIds
        /// selon l'appartenance réelle aux tables chargées.</summary>
        public List<int> RefIds { get; set; } = new();          // cols 40-49
        /// <summary>Sous-ensemble de RefIds présent dans S_Spell
        /// (rempli par MonsterService à l'enrichissement).</summary>
        public List<int> SpellIds { get; set; } = new();
        /// <summary>Sous-ensemble de RefIds présent dans
        /// S_ClassDropItem — drops "breakpoint" liés à la classe.</summary>
        public List<int> ClassDropIds { get; set; } = new();
        /// <summary>Cellules d'aggro (col 37 0-idx → data+0x60 →
        /// template+0x20) — rayon de détection passive en <b>cellules</b> ;
        /// la conversion native le multiplie par DAT_087cc688 = 0.5 u.
        /// Preuve : `CAI::DoSeekEnemy` passe `template+0x20` à
        /// `CMap::CircularRangeSearch`, et la conversion
        /// CMonsterData→CMonsterTemplate écrit +0x20 = col37 × 0.5
        /// (zone @0x8367a5).</summary>
        public int AggroCells { get; set; }
        /// <summary>Rayon d'aggro en unités monde (AggroCells × 0.5,
        /// formule native de la conversion template).</summary>
        public float AggroRadius => AggroCells * 0.5f;
        /// <summary>Profil S_Ai du monstre (col 38 0-idx → data+0x64 →
        /// template+0x86, lu par `CMonsterTemplate::GetAiId`
        /// @0x84911a0 — l'AI instanciée à la naissance du mob vit en
        /// CMonster+0x328 ; son octet +5 = S_Ai col2 pilote la chance
        /// de détection passive dans `CAI::DoSeekEnemy`).</summary>
        public int AiId { get; set; }
        /// <summary>Butin d'or — S_Monster ne porte PAS de colonnes
        /// d'or : le natif tire l'or du header S_DropItem du mob
        /// (GiveGold @08497200 : entrée {chance, centre, spread} issue
        /// des cols 4-6 de la ligne de drop). Voir
        /// DropTable.GoldChancePct/GoldCenter/GoldSpread.</summary>
        public string[] Raw { get; set; } = System.Array.Empty<string>();

        public bool IsBoss => HP >= 1_000_000;

        /// <summary>Masque de faiblesses Break Point — S_Monster col idx 10
        /// (1-idx 11) → <c>CMonsterData+0x28</c>, 9 bits : bit (attr−1)
        /// pour attr 1..9 = Slash, Strike, Pierce, Fire, Ice, Lightning,
        /// Nature, Light, Dark (ordre prouvé en croisant 139/156 bosses
        /// du wiki « Break Points= » ; cf. <c>BreakPointRules</c>).
        /// Lu par <c>CalculatePatienceAttrDamage</c> @0x083f4260 :
        /// <c>(mask &amp; (1 &lt;&lt; (attr−1))) != 0</c>. 0 = jamais
        /// cassable par drain d'attribut.</summary>
        public int BpWeakMask { get; set; }
        /// <summary>Masque « gate niveau » (col idx 11 → <c>+0x2c</c>,
        /// octet) : 0xF = pas de filtre, sinon bit (1&lt;&lt;tier) de
        /// l'écart de niveaux attaquant/mob requis pour drainer.</summary>
        public int BpGate { get; set; }
        /// <summary>Patience (jauge BP) maximale — col idx 12 →
        /// <c>+0x30</c>. <c>CDeserialize</c> @0x0860d790 : si masque ET
        /// patience valent 0 → 100. Cap 9 999 999
        /// (<c>MaxPatience</c> @0x083f3d30).</summary>
        public int Patience { get; set; } = 100;
        /// <summary>Le mob possède une barre BP — <c>mob+0x7d</c> =
        /// (patience != 0) posé par <c>BornMonster</c> @0x084b02b0.
        /// Un masque nul laisse la barre « pleine à vie » (jamais
        /// drainée) : seul <see cref="BpWeakMask"/> != 0 la rend
        /// cassable.</summary>
        public bool HasBpBar => Patience != 0;
        /// <summary>Vrai si le mob peut être brisé (une faiblesse + une
        /// barre) — les « mobs BP » (≈1 560 lignes S_Monster : donjons,
        /// coffres de trésor, élites).</summary>
        public bool CanBreak => BpWeakMask != 0 && Patience > 0;

        /// <summary>Mob immobile (coffre, autel, objet de quête) —
        /// dérivé des données : les mobs-objets n'ont NI attaque NI
        /// portée (c14/c16 vides — 526 entrées : statues, coffres,
        /// autels 999999 HP). Le natif ne leur donne aucun état FSM
        /// de déplacement ; notre wander les baladait comme des mobs
        /// normaux (bug live : "les coffres se déplacent").</summary>
        public bool IsStationary => AttackPower <= 0
            && AttackRangeMax <= 0;

        /// <summary>Vitesse de déplacement hors combat (col 34 0-idx →
        /// <c>CMonsterData+0x5c</c> → <c>CMonsterTemplate+0x1c</c> =
        /// valeur × <c>DAT_087cc688</c>=0.5, conversion
        /// @0x8367a5). <c>GetMovement</c> (vtable+0xd8 →
        /// <c>CCharFormula::Movement</c> @0x83f3e10) la renvoie tant
        /// que le flag 0x10 de monstre+0x3c n'est pas posé — c'est le
        /// float4 du 298 ClientSeeMob pour un mob idle.</summary>
        public float RoamSpeed { get; set; }
        /// <summary>Vitesse en combat/roaming actif (col 35 0-idx →
        /// <c>CMonsterData+0x5d</c> → <c>CMonsterTemplate+0x18</c> =
        /// valeur × 0.5). <c>CElfPet::GetMovement</c> utilise aussi
        /// ce champ (CCharFormula::Movement(CElfPet) @0x83f3eb0).</summary>
        public float BattleSpeed { get; set; }
        /// <summary>Masque de localisation — S_Monster col idx 59
        /// (60e token de <c>CMonsterQuery::Deserialize</c> @0x860d790 →
        /// <c>CMonsterData+0xb8</c>). La zone native ignore la ligne
        /// [reborn_monsters] si <c>mask != 0 &amp;&amp; ((mask &gt;&gt;
        /// (Locate&amp;0x1f)) &amp; 1) == 0</c>
        /// (<c>GetAllRebornMonsterInfo</c> @0x083688a0) : c'est le
        /// marqueur « mob régional / event » — les 62 valeurs non nulles
        /// (2=HK,16=MY…) sont toutes des mobs de festival/test.
        /// Bit 5 = US (ConvertLocate @0x08589260).</summary>
        public int LocateMask { get; set; }

        /// <summary>Vrai si le mob peut apparaître pour la localisation
        /// <paramref name="locate"/> du serveur (règle native).</summary>
        public bool VisibleAtLocate(int locate) =>
            LocateMask == 0 || ((LocateMask >> (locate & 0x1f)) & 1) != 0;

        /// <summary>Entité « décor » non-combattante : les mobs-objets
        /// de quête (coffres atk=1/range=1 comme 20840, œufs, cristaux
        /// ai=164 des invasions de légion) ne doivent ni se déplacer ni
        /// attaquer — ils restent ATTAQUABLES par les joueurs (quêtes).
        /// Règle : attaque ≤1 (décorative) OU aucune portée OU le
        /// profil AI 164 (cristaux de bastion — seek=100 mais modèle
        /// fixe, le natif ne les fait jamais marcher).</summary>
        public bool IsStructure => AttackPower <= 1
            || AttackRangeMax <= 0 || AiId == 164;

        /// <summary>Cristal de bastion « allié » (bleu) — ai=164 ET
        /// sorts embarqués (RefIds 45404/58023) : le cristal bleu est
        /// un objectif de la légion, pas une cible joueur — le natif ne
        /// le laisse pas être attaqué par les joueurs (seul le rouge,
        /// sans sorts, est attaquable). Données : paires 55xxx/60xxx
        /// « …據點水晶 » des scènes monde.</summary>
        public bool IsAlliedCrystal =>
            AiId == 164 && RefIds.Count > 0;
    }

    /// <summary>Patience max d'une ligne : champ idx 12 lu comme uint
    /// (les valeurs décimales « 54023.74166 » sont tronquées comme le
    /// <c>operator&gt;&gt;(uint*)</c> natif), 0+masque 0 → 100, plafond
    /// 9 999 999 (<c>CCharFormula::MaxPatience</c> @0x083f3d30).</summary>
    public static int ParsePatience(string[] row)
    {
        var mask = IniTable.GetInt(row, 10);
        long pat = 0;
        if (row.Length > 12 && double.TryParse(row[12],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)
            && d > 0)
            pat = (long)d;
        if (mask == 0 && pat == 0) pat = 100;
        return (int)Math.Min(pat, 9_999_999);
    }

    public static List<MonsterDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var monsters = new List<MonsterDef>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (row.Length < 20 || !int.TryParse(row[0], out var id)) continue;

            var m = new MonsterDef
            {
                Id = id,
                ModelCode = row.Length > 1 ? row[1] : string.Empty,
                Name = row.Length > 2 ? row[2] : string.Empty,
                Family = IniTable.GetInt(row, 3),
                Level = IniTable.GetInt(row, 4),
                // col 6 (1-idx) = type de monstre (CEC_MonsterTypeCheck
                // natif) : 1 = normal, 2 = élite/mini-boss, 3 = boss,
                // 4-5 = spéciaux. Distribution réelle : 1×6960, 2×1196,
                // 3×1444 (dont 20001-20004 lv80), 4×386, 5×47.
                MonsterType = IniTable.GetInt(row, 5),
                // col 4 (1-idx) → CMonsterData+0x10 = catégorie DSL
                // KillMonster (preuve : 3ᵉ token du Deserialize
                // @0x860d790 écrit via this+0x20).
                KillType = IniTable.GetInt(row, 3),
                // col 61 (1-idx) → CMonsterData+0xc4 = score DoD
                // par kill (preuve : 61ᵉ token, lecture version>4 ;
                // GainKillScore param_2 @0x845c1e0). La col 50 (0-idx)
                // est la chance de summon (data+0x98), PAS le score —
                // corrigé au lot13 sur preuve désasm @0x860d790.
                KillScore = IniTable.GetLong(row, 60),
                Enabled = IniTable.GetInt(row, 7) != 0,
                HP = long.TryParse(IniTable.GetStr(row, 8), out var hp) ? hp : 0,
                MP = long.TryParse(IniTable.GetStr(row, 9), out var mp) ? mp : 0,
                AttackType = IniTable.GetInt(row, 13),
                // col 54 (1-idx) = EXP — preuve AttribLog natif :
                // 56001 Gain:25 = col54, 56211 Gain:36 = col54.
                ExpReward = IniTable.GetLong(row, 53),
                // col 55 (1-idx) = NP/class-exp — preuve AttribLog
                // natif : 56001 NP Gain:5 = col55.
                ClassExp = IniTable.GetLong(row, 54),
                // Mapping prouvé par CMonsterQuery::Deserialize
                // @0x860da50 + conversion CMonsterData→CMonsterTemplate
                // (croisé données réelles : 56001 atk 186 col14,
                // variance 10 col15, def 47 col18 ; boss 20001 atk
                // 53641 col14) :
                //   col13→element(+0x70), col14→attaque(+0x4c),
                //   col15→variance(+0x44), col16→portée(+0x40),
                //   col17→vit.attaque(+0x38), col18→def phys(+0x5c),
                //   col19→hit(+0x52), col20→dodge(+0x50),
                //   col21→crit phys(+0x54), col22→critdmg(+0x58),
                //   col23→crit mag, col24→critdmg mag,
                //   cols25-33→résistances(+0xa8..+0xb0).
                //   +0x60 (def magique) n'est pas écrit par la
                //   conversion → 0 natif.
                AttackElement = IniTable.GetInt(row, 13),
                AttackPower = IniTable.GetInt(row, 14),
                AttackVariance = IniTable.GetInt(row, 15),
                AttackRangeMax = IniTable.GetInt(row, 16),
                AttackSpeed = IniTable.GetInt(row, 17),
                PhysicalDef = IniTable.GetInt(row, 18),
                Hit = IniTable.GetInt(row, 19),
                Dodge = IniTable.GetInt(row, 20),
                PhysicalCrit = IniTable.GetInt(row, 21),
                PhysicalCritDamage = IniTable.GetInt(row, 22),
                MagicCrit = IniTable.GetInt(row, 23),
                MagicCritDamage = IniTable.GetInt(row, 24),
                MagicDef = 0,
                // col 38 (0-idx 37) → data+0x60 → template+0x20 =
                // rayon d'aggro en cellules (×0.5 u — conversion
                // template @0x8367a5, DAT_087cc688=0.5).
                AggroCells = IniTable.GetInt(row, 37),
                // col 39 (0-idx 38) → data+0x64 → template+0x86 =
                // profil S_Ai (`GetAiId` @0x84911a0).
                AiId = IniTable.GetInt(row, 38),
                // cols 35/36 (0-idx 34/35) → data+0x5c/0x5d →
                // template+0x1c/+0x18 = roam/battle speed (×0.5,
                // conversion @0x8367a5 — utilisées par GetMovement
                // pour le float4 du 298).
                RoamSpeed = IniTable.GetInt(row, 34) * 0.5f,
                BattleSpeed = IniTable.GetInt(row, 35) * 0.5f,
                // col 60 (0-idx 59) → data+0xb8 = masque Locate
                // (filtre natif du spawn, GetAllRebornMonsterInfo).
                LocateMask = IniTable.GetInt(row, 59),
                // cols 11-13 (1-idx) → data+0x28 / +0x2c / +0x30 :
                // masque faiblesses, gate niveau, patience max
                // (CMonsterQuery::Deserialize @0x0860d790 l.97-102).
                BpWeakMask = IniTable.GetInt(row, 10),
                BpGate = IniTable.GetInt(row, 11),
                Patience = ParsePatience(row),
                Raw = row,
            };

            for (var i = 0; i < 10; i++)
                m.CombatStats[i] = IniTable.GetInt(row, 15 + i);
            for (var i = 0; i < 9; i++)
                m.Resistances[i] = IniTable.GetInt(row, 25 + i);
            for (var i = 0; i < 5; i++)
                m.AuxStats[i] = IniTable.GetInt(row, 34 + i);
            // Cols 40-49 : références brutes (spells/enchants/classdrops)
            // — le tri par table réelle est fait dans MonsterService.
            for (var i = 40; i <= 49 && i < row.Length; i++)
            {
                var v = IniTable.GetInt(row, i);
                if (v > 0) m.RefIds.Add(v);
            }
            monsters.Add(m);
        }

        return monsters;
    }
}
