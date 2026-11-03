using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Spell.ini — sorts et buffs (|V.8|45|, 45 cellules/ligne).
///
/// Mapping prouvé par `GameData::CSpellQuery::Deserialize` @0x862af40
/// (comptage lecture par lecture : `operator>>(longlong)` lit **une
/// seule cellule** — c23 contient déjà l'i64 complet, ex. 50011
/// →54095984974762049) puis par `CSpellDBO::LoadAllSpellData`
/// @0x8380130 qui copie `CSpellData` → `SSpellData` :
///   c17→+0x34→+0x08 FAMILLE          c18→+0x38→+0x0c TYPE dispatch
///   c19→+0x3c i64→+0x10/+0x14 flags  c20→+0x44→+0x18/+0x1a cast-shape
///   c21→+0x48→+0x1c                  c22→+0x4c u8→+0x20 req level
///   c23→+0x50 i64→+0x24/+0x28 mask   c24→+0x58→+0x30
///   c25→+0x5c→+0x34                  c26→+0x64→+0x38 cast ds
///   c27→+0x68→+0x3c down-time ds     c28→+0x6c→+0x40 float (portée)
///   c29→+0x6e→+0x48 durée×const      c32→+0x74→+0xbc MP base
///   c33→+0x76→+0xbe élément          c35→+0x8c→+0xc0 power
///   c36→+0x8e→+0xc2 heal power       c37→+0x90→+0xc4 variance
///   c39→+0xb0→gate résultat          c41→+0xb8→+0x84 valeur (-9)
///   c42→+0xbc→+0xcc pay type         c43→+0xc0→+0x88 upgrade base
/// Les cellules string (c2/c4-7/c9-13/c15/c16-nom/c44) avancent le
/// curseur d'une cellule chacune — le nom Big5 est c16 → +0x30.
/// Dispatch natif `ISpell::OnHitLife` @0x84f2080 : `+0xc∈{4,5}` (c18)
/// + flags hit `+0x94` (dérivés du type par `SSpellData::Init`) →
/// HitRestoreSkill / HitPhysicalSkill / HitMagicSkill.
/// </summary>
public static class SpellParser
{
    public sealed class SpellDef
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;     // J001
        /// <summary>Blast/motion group id (col 3 → `SSpellData+0xa0`).
        /// Émis tel quel dans le field +0x20 du 288 ClientSpellBlastOK —
        /// le client résout l'animation du sort via son KFM
        /// (CCharKfm::GetBlastID désasm @0x080e03f0 ; capture : sort
        /// 50022 col3=3092 → 288 porte 0x0c14 à l'offset blastId).
        /// 0 pour les skills physiques sans blast group.</summary>
        public int BlastId { get; set; }
        /// <summary>La c8 (idx 7, `sNNNNN` de projectile) est non-vide →
        /// le sort lance un projectile qui vole jusqu'à la cible avant
        /// l'impact (`SSpellData+0xa4 & 1` ; désasm Init
        /// @0x084eecf0). Le délai d'impact ajoute alors
        /// distance/<see cref="FlySpeed"/>.</summary>
        public bool HasProjectile { get; set; }
        /// <summary>Vitesse de vol u/s (c9 u32 → `SSpellData+0xa8` =
        /// raw × 0.5). 0 → `CSpellToLife::GetBlastTime` @0x084f7db0
        /// retombe sur `DefaultFlySpeed` = 40 u/s.</summary>
        public float FlySpeed { get; set; }
        public string Category { get; set; } = string.Empty; // col 15 ('O')
        public string Name { get; set; } = string.Empty;     // Big5 c16
        /// <summary>Famille de sort (col 17 → `CSpellData+0x34` →
        /// `SSpellData+0x08`). 6/7 = sorts de classe (gate class_lv à
        /// l'apprentissage) ; 1/2/4 = familles physiques/soin/commun.
        /// Vérifié : Onrush 50038→6, 火矢 50126→6, Cure 50028→4,
        /// 珠寶融合 50269→7.</summary>
        public int Family { get; set; }                      // col 17
        /// <summary>Type de dispatch natif (col 18 → `CSpellData+0x38`
        /// → `SSpellData+0x0c`). Lu par `SSpellData::Init` pour calculer
        /// les hit-flags `+0x94` ({6,7}→0x0c, {4,5}→0x44, {2,3}→0x24,
        /// autre→0x04) et testé par `OnHitLife` @0x84f2080 :
        /// `type∈{4,5}` → `HitRestoreSkill`. Vérifié : heals
        /// 50028/50055/50109→4, Slam/Onrush/火矢→1.</summary>
        public int Type { get; set; }                        // col 18
        /// <summary>Alias de <see cref="Type"/> (col 18) — les appelants
        /// `TargetType==1` testent le cas « attaque hostile ».</summary>
        public int TargetType { get; set; }                  // col 18
        /// <summary>Flags/OpFlags i64 du sort (col 19 → `CSpellData+0x3c`
        /// i64 → `SSpellData+0x10/+0x14`) — testé par
        /// `SSpellData::CheckOpFlag` @0x84efae0 (conditions de cast :
        /// état entité, cible, monture…).</summary>
        public ulong OpFlags { get; set; }                   // col 19
        /// <summary>Sélecteur de cast-shape (index 20 = col 21 →
        /// `CSpellData+0x44`) — le natif en dérive `SSpellData+0x18`
        /// par switch dans `LoadAllSpellData` @0x08380130, combiné à
        /// <see cref="ShapeSub"/> (+0xcc) : 0→{3,5}, 1-3→3,
        /// 4→{0,2,5,6}, 5→{1,2,5,6}, 6→0. Shapes : 0=cible unique,
        /// 1=proche (autour du lanceur), 2=direction, 5=croix,
        /// 6=secteur — `CastToLife` @0x081545a0 redirige 2/5/6 vers
        /// les AoE à la position de la cible.</summary>
        public int Param1 { get; set; }                      // index 20 (col 21)
        /// <summary>Col 22 → `CSpellData+0x48` → `SSpellData+0x1c`
        /// (paramètre auxiliaire de la forme — 14336 typique).</summary>
        public int Param2 { get; set; }                      // index 21 (col 22)
        /// <summary>Sous-sélecteur de shape (index 30 = col 31 →
        /// `CSpellData+0xcc`, version ≥5) — combiné à
        /// <see cref="Param1"/> dans le switch `LoadAllSpellData`
        /// (c31==1→direction, ==2→secteur, ==3→croix pour Param1∈{4,5}).
        /// </summary>
        public int ShapeSub { get; set; }                    // index 30 (col 31)
        /// <summary>Rayon de zone ×2 (index 29 = col 30 →
        /// `CSpellData+0x6e` → `SSpellData+0x48 = (short)(u16 × 0.5f)`
        /// dans LoadAllSpellData @0x08380130 — `DAT_087cc688`=0.5f).
        /// Rayon du HitTest circulaire des sorts de zone
        /// (`CSpellToLocation::HitTest` @0x084f9300), pas une durée —
        /// les AoE portent 10-50 ici (rayon 5-25 u).</summary>
        public int Duration { get; set; }                    // index 29 (col 30)
        /// <summary>Sort/enchant associé (col 39 → `CSpellData+0xb0`,
        /// gate du bloc résultat dans LoadAllSpellData — id S_Enchant
        /// 5xxxx appliqué à l'impact).</summary>
        public int ResultSpellId { get; set; }               // col 39
        /// <summary>Chance d'application de l'enchant résultat en %
        /// (col 40 → `CSpellData+0xb4`) — 0/vide = toujours. Les
        /// debuffs de sorts de mobs portent 15/50/100 ici ; le natif
        /// roll `rand%100 < chance` avant AddEnchant.</summary>
        public int ResultSpellChance { get; set; }           // col 40
        /// <summary>Valeur (col 41 → `CSpellData+0xb8` → `SSpellData+0x84`,
        /// défaut natif -9 si cellule 0/vide).</summary>
        public int Value { get; set; }                       // col 41
        /// <summary>Champ i32 « puissance » du 288 ClientSpellBlastOK —
        /// alimenté par col 27 (down-time ds → `SSpellData+0x3c`).
        /// Vérifié capture : 50022 → 20.</summary>
        public int Power { get; set; }                       // col 27
        /// <summary>Base du coût MP (col 32 → `CSpellData+0x74` →
        /// `SSpellData+0xbc` s16). Coût natif `CCharFormula::CostMp`
        /// @0x83f44d0 : `MpCoef(palier col1 S_SpellReference) × base
        /// × 0.01`, modulé par BuffCostRate du lanceur. Vérifié :
        /// Slam 50016→75, Cure 50028→112, 火矢 50126→100.</summary>
        public int MpCost { get; set; }                      // col 32
        /// <summary>Portée de cast (col 28 → `CSpellData+0x6c` →
        /// `SSpellData+0x40 = u16 × const` float — portée : melee=4,
        /// heals=40, nukes=36, cohérent avec la poursuite client).
        /// Vide → repli sur la portée de l'arme.</summary>
        public int Range { get; set; }                       // col 28
        /// <summary>Niveau de classe requis (col 22 → `CSpellData+0x4c`
        /// u8 → `SSpellData+0x20`, testé par `SSpellData::CheckLevel`
        /// @0x84edfe0 : `+0x20 <= classLevel`). Vérifié : Slam→1,
        /// Cure→3, Revive→9.</summary>
        public int RequiredLevel { get; set; }               // col 22
        /// <summary>Mask de classes i64 (col 23 → `CSpellData+0x50`
        /// i64 → `SSpellData+0x24/+0x28`, bit(classId-1)) —
        /// `SSpellData::CheckClass` @0x84ee000 et `LoadAllCommonSpell`
        /// @0x83732b0. UNE seule cellule — c24 est un champ distinct
        /// (+0x58→+0x30). Vérifié : Onrush 50038→1 (Warrior),
        /// Ambush 50058→16 (Thief bit4), 火矢 50126→2 (Mage),
        /// J594 50269→0x3FFFF (18 classes).</summary>
        public ulong ClassMask { get; set; }                 // col 23
        /// <summary>Temps d'incantation en ds (col 26 → `CSpellData+0x64`
        /// → `SSpellData+0x38` s16). `ISpell::OnCast` @0x84ef430 :
        /// `psVar10[0x1c]` (+0x38) == 0 → instantané ; sinon
        /// `CastTime(entity, base)` clampé [4,30000] ds. Vérifié :
        /// heals 20-25 (2-2.5 s), attaques vides = instantané.</summary>
        public int CastTimeDs { get; set; }                  // col 26
        /// <summary>Compat : cast en ms (plancher natif 4ds → 400ms).</summary>
        public int CastTimeMs => CastTimeDs is > 0 and var c
            ? Math.Max(c, 4) * 100 : 0;
        /// <summary>Down-time/cooldown en ds (col 27 → `CSpellData+0x68`
        /// → `SSpellData+0x3c` u32). Armé par
        /// `CCharacter::PutInDownTime` @0x842f380 après cast réussi :
        /// `CoolDownTime(entity, base)` @0x83f6060 =
        /// `(1−BuffCoolDownRate)×base`, plancher 5 ds si base>0.
        /// Vérifié : Slam 20 (2 s), 火矢 30, Onrush 80, heals vide.</summary>
        public int CooldownDs { get; set; }                  // col 27
        /// <summary>Monnaie d'upgrade (col 42 → `CSpellData+0xbc` →
        /// `SSpellData+0xcc` u16, désasm `CSpellMasterFactory::
        /// SetSpellLevel` @0x8156280 : 1 = or (GainGold), 9 = points de
        /// classe (GainNp)).</summary>
        public int PayType { get; set; }                     // col 42
        /// <summary>Base du coût d'upgrade (col 43 → `CSpellData+0xc0`
        /// → `SSpellData+0x88`). Coût natif = base × taux(palier)/100 —
        /// le taux vient de S_SpellReference (col30 CP / col31 or).</summary>
        public int UpgradeCostBase { get; set; }             // col 43
        /// <summary>Puissance de dégât direct du sort (col 35 →
        /// `CSpellData+0x8c` → `SSpellData+0xc0` s16) — gate
        /// `+0xc0 != 0` dans `HitMagicSkill`/`HitPhysicalSkill` puis
        /// terme `power × refCoef(col2/col3)/100`. Vérifié :
        /// Slam→120, 火矢→150, Ambush→110 ; vide sur les heals.</summary>
        public int DamagePower { get; set; }                 // col 35
        /// <summary>Puissance de soin du sort (col 36 → `CSpellData+0x8e`
        /// → `SSpellData+0xc2` s16) — lue par `HitRestoreSkill`
        /// @0x84f1f40 (gate != 0) → `CalculateMagicSkillHeal`
        /// @0x83f9310 : `heal = power × refCoef(col4)/100`.
        /// Vérifié : Cure→110, 治療之光→90, 神癒之光→80.</summary>
        public int HealPower { get; set; }                   // col 36
        /// <summary>Élément du sort (col 33 → `CSpellData+0x76` →
        /// `SSpellData+0xbe` s16) — utilisé par `SSpellData::Init` pour
        /// les hit-flags et par `CalculateMagicSkillDamage` pour la
        /// résistance si le lanceur n'a pas d'élément d'enchant.
        /// Vérifié : melee→1 (physique), 火矢→6 (foudre), heals→10.</summary>
        public int AttackElement { get; set; }               // col 33
        /// <summary>Variance des dégâts/soins (col 37 → `CSpellData+0x90`
        /// → `SSpellData+0xc4` = lo8 signé étendu) — terme
        /// `rand%(var×10000)×0.0001` des formules natives ; 0 → fixe.
        /// Vérifié : 15 typique, 20 sur 火矢.</summary>
        public int DamageVariance { get; set; }              // col 37
        /// <summary>Groupe de sort elf (col 34 → `CSpellData+0xe0`) —
        /// clé de l'<c>ElfPetSpellGroupMap</c> natif
        /// (<c>CGlobalInfo::InitElfPetSpellGroupMap</c> @0x80db390 :
        /// `puVar6[0x38]` = +0xe0, restreint aux sorts famille 8 par
        /// l'assert `puVar6[0xd]==8`). Les livres « 戰魂技能書 »
        /// (commande 6037 ChangePetBattleSpellPlus) piochent un sort
        /// aléatoire du groupe. Données : seul le groupe test 999 est
        /// peuplé (8 sorts 58668-58698).</summary>
        public int ElfGroup { get; set; }                    // col 34
        /// <summary>Flag race (col 14 → `SSpellData+0x2c`, KB-07) :
        /// -1/0 = sans restriction ; sinon 2 bits par race (Humain 3,
        /// Zumi 4, Anuran 16, Ursun 64, Halfkin 768 ; 855 = toutes) —
        /// `LoadAllCommonSpell` @0x83732b0. Vu sur 4 236 sorts à -1 et
        /// 249 raciaux (S_Spell V.8).</summary>
        public int RaceFlag { get; set; }                    // col 14
        public string[] Raw { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>Parse la table indexée par spell id (col 0).</summary>
    public static List<SpellDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var spells = new List<SpellDef>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (row.Length < 20 || !int.TryParse(row[0], out var id)) continue;

            spells.Add(new SpellDef
            {
                Id = id,
                Code = IniTable.GetStr(row, 1),
                BlastId = IniTable.GetInt(row, 3),
                // idx7 → +0x1c/+0xa4&1 = projectile ; idx8 → +0xd0
                // u32 → +0xa8 vitesse = raw × 0.5 (Init @0x084eecf0).
                HasProjectile = row.Length > 7 &&
                    !string.IsNullOrWhiteSpace(row[7]),
                FlySpeed = IniTable.GetInt(row, 8) * 0.5f,
                Category = IniTable.GetStr(row, 15),
                Name = IniTable.GetStr(row, 16),
                // c17→SSpellData+0x08 = famille ; c18→+0x0c = type dispatch.
                Family = IniTable.GetInt(row, 17),
                Type = IniTable.GetInt(row, 18),
                TargetType = IniTable.GetInt(row, 18),
                Param1 = IniTable.GetInt(row, 20),
                Param2 = IniTable.GetInt(row, 21),
                ShapeSub = IniTable.GetInt(row, 30),
                // c19 = flags i64 → +0x10/+0x14 (CheckOpFlag natif).
                OpFlags = ulong.TryParse(
                    IniTable.GetStr(row, 19), out var opf) ? opf : 0,
                // c29→+0x6e→+0x48 = durée ; c39→+0xb0 = sort résultat ;
                // c40→+0xb4 = chance % ; c41→+0xb8→+0x84 = valeur
                // (défaut -9 natif).
                Duration = IniTable.GetInt(row, 29),
                ResultSpellId = IniTable.GetInt(row, 39),
                ResultSpellChance = IniTable.GetInt(row, 40),
                Value = row.Length > 41 &&
                    int.TryParse(row[41], out var v) ? v : -9,
                Power = IniTable.GetInt(row, 27),
                // c32→+0x74→+0xbc = base coût MP ; c28→+0x6c→+0x40 = portée.
                MpCost = IniTable.GetInt(row, 32),
                Range = IniTable.GetInt(row, 28),
                RequiredLevel = IniTable.GetInt(row, 22),
                // c23 = mask i64 mono-cellule → +0x24/+0x28.
                ClassMask = ulong.TryParse(
                    IniTable.GetStr(row, 23), out var mask) ? mask : 0,
                // c26→+0x64→+0x38 = cast ds ; c27→+0x68→+0x3c = down-time ds.
                CastTimeDs = IniTable.GetInt(row, 26),
                CooldownDs = IniTable.GetInt(row, 27),
                // c42→+0xbc→+0xcc = pay type ; c43→+0xc0→+0x88 = base coût.
                PayType = IniTable.GetInt(row, 42),
                UpgradeCostBase = IniTable.GetInt(row, 43),
                // c35→+0x8c→+0xc0 = power ; c36→+0x8e→+0xc2 = heal ;
                // c33→+0x76→+0xbe = élément ; c37→+0x90→+0xc4 = variance.
                DamagePower = IniTable.GetInt(row, 35),
                HealPower = IniTable.GetInt(row, 36),
                AttackElement = IniTable.GetInt(row, 33),
                DamageVariance = IniTable.GetInt(row, 37),
                // c34→+0xe0 = groupe de sort elf (map
                // ElfPetSpellGroupMap native).
                ElfGroup = IniTable.GetInt(row, 34),
                RaceFlag = IniTable.GetInt(row, 14),
                Raw = row,
            });
        }

        return spells;
    }
}
