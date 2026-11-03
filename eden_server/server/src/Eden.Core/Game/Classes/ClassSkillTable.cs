using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Classes;

/// <summary>
/// Octroi des "class skills" et "general skills" d'une classe —
/// modèle natif reconstruit depuis zone/decompiled.c :
///
///   `CCharacter::AddClass` apprend les spellmasters via
///   `CSpellFactory::SimpleGetSpell` ; `CSpellFactory::GetClassSpell`
///   itère le vecteur de la classe (S_Class.ini cols 30-48) et
///   `GetCommonSpell` les spellmasters indexés par mask de classe
///   (`LoadAllCommonSpell` : chaque spellmaster S_Spell porte un mask
///   u64 col 23 = bit(classId-1)).
///
/// Règle d'octroi (vérifiée contre la capture des 23 spellmasters
/// d'un perso frais warrior+19) :
///   candidat = vecteur S_Class(classe) ∪ { s : s.ClassMask == bit }
///   filtré par `1 <= s.RequiredLevel <= classLevel` (S_Spell col 22,
///   `SSpellData::CheckLevel`), puis dédupliqué par code : les entrées
///   J-codées sont des paliers d'un même certificat (J504 → 50201,
///   50239, 50272, 50306, 50338) — seul le plus petit id est le
///   spellmaster ; les paliers suivants montent `player_spells
///   .spell_level`. HYPOTHESIS: la borne de palier certificat n'est pas
///   dans S_Spell — les 5 entrées partagent req=1 ; seul le 1er id est
///   octroyé (cohérent avec la capture : 50201 seul).
///
/// Preuves mask (S_Spell col 23) : 50038→1 Warrior, 50126→2 Mage,
/// 50078→4 Hunter, 50102/50106→8 Cleric, 50058→16 Thief.
/// Vecteurs S_Class (cols 30-48) lus par ClassStatsParser ; les
/// classes 3-5 apparaissent "vides" si le fichier est découpé ligne
/// par ligne — les descriptions Big5 contiennent des LF nus ; le
/// join multi-lignes d'IniTable les restaure (305 lignes complètes).
/// </summary>
public static class ClassSkillTable
{
    /// <summary>
    /// Spellmasters à apprendre pour <paramref name="classId"/> au
    /// niveau de classe <paramref name="classLevel"/> — union du
    /// vecteur S_Class et des spellmasters à mask mono-classe,
    /// filtrée par niveau requis et dédupliquée par code de sort.
    /// Les ids déjà présents dans player_spells doivent être ignorés
    /// par l'appelant (LearnSpell incrémenterait spell_level).
    /// </summary>
    public static IReadOnlyList<int> GrantableSpells(int classId,
        int classLevel, IReadOnlyList<SpellParser.SpellDef> spells,
        ClassStatsParser.ClassDefinition? classDef, int raceId = 0)
    {
        // bit natif : LoadAllCommonSpell indexe par bit(classId-1).
        var bit = classId is >= 1 and <= 63 ? 1UL << (classId - 1) : 0UL;
        var byId = new Dictionary<int, SpellParser.SpellDef>();
        foreach (var s in spells) byId[s.Id] = s;

        // KB-07 : avec une race connue, une variante raciale d'une autre
        // race n'est jamais candidate — sinon le dédoublonnage par code
        // (Min) retient la variante Humaine pour tout le monde.
        bool Eligible(int id) =>
            byId.TryGetValue(id, out var s) &&
            s.RequiredLevel >= 1 && s.RequiredLevel <= classLevel &&
            RaceAllows(s.RaceFlag, raceId);

        var candidates = new List<int>();
        var seen = new HashSet<int>();

        // 1. Vecteur de la classe (S_Class cols 30-48 = +0x50/+0x5c
        //    natifs itérés par GetClassSpell — les ids hors S_Spell
        //    comme les enchants 52xxx sont filtrés par Eligible →
        //    lookup byId).
        if (classDef != null)
            foreach (var id in classDef.ClassSpellIds)
                if (Eligible(id) && seen.Add(id))
                    candidates.Add(id);

        // 2. Spellmasters à mask strictement mono-classe (class skills
        //    privées — couvre aussi les classes dont le vecteur
        //    S_Class serait absent).
        if (bit != 0)
            foreach (var s in spells)
                if (s.ClassMask == bit && Eligible(s.Id) && seen.Add(s.Id))
                    candidates.Add(s.Id);

        // 3. Dédupliquage par code : les chaînes J (certificats) ont 5
        //    entrées au même code — le spellmaster = plus petit id.
        //    Les codes vides ne fusionnent pas (clé unique par id).
        return candidates
            .GroupBy(id => byId[id].Code is { Length: > 0 } c ? c : $"#{id}")
            .Select(g => g.Min())
            .OrderBy(id => id)
            .ToList();
    }

    /// <summary>KB-07 : le flag race d'un sort (<c>S_Spell</c> col 14)
    /// laisse-t-il passer la race <paramref name="raceId"/> (1-based) ?
    /// Sans restriction (flag ≤ 0) ou race inconnue (≤ 0) : oui.</summary>
    public static bool RaceAllows(int raceFlag, int raceId) =>
        raceFlag <= 0 || raceId <= 0 ||
        (raceFlag & (3 << (2 * (raceId - 1)))) != 0;

    /// <summary>KB-06 : ligne <c>S_Class</c> du personnage = bande de sa
    /// race, <c>(raceId-1)*100 + classId</c> (formule native
    /// <c>(nibble&gt;&gt;1)×100+classId</c>) ; repli sur la ligne
    /// <paramref name="classId"/> (race inconnue/0, table sans bande).
    /// L'ancien test <c>RaceId*100+cls || Id==cls</c> ne matchait jamais
    /// la bande et retombait toujours sur la ligne Humaine.</summary>
    public static ClassStatsParser.ClassDefinition? ClassFor(
        IEnumerable<ClassStatsParser.ClassDefinition> classes,
        int raceId, int classId)
    {
        var row = (Math.Max(raceId, 1) - 1) * 100 + classId;
        ClassStatsParser.ClassDefinition? fallback = null;
        foreach (var c in classes)
        {
            if (c.Id == row) return c;
            if (c.Id == classId) fallback = c;
        }
        return fallback;
    }

    /// <summary>
    /// Spellmasters "généraux" d'une classe (skills de couleur —
    /// jaune mêlée, orange distance, vert support, bleu magie,
    /// violet spécial) — modèle natif `CSpellDBO::LoadAllCommonSpell`
    /// (@0x83732b0) : la table commune indexe chaque spellmaster par
    /// son bit de classe ; `GetCommonSpell` (@0x8157320) les octroie
    /// via `SimpleGetSpell(id, char+0x470)` — gaté sur le NIVEAU
    /// PERSONNAGE (et non la classe comme `GetClassSpell`/+0x47e).
    /// Un sort est "général" quand son mask col23 contient le bit de
    /// la classe ET d'autres bits (partagé par plusieurs classes de
    /// la même couleur — 50011 mask 0xc0300300321041 couvre toutes
    /// les classes physiques). Les mono-masques restent des class
    /// skills (GrantableSpells).
    /// </summary>
    public static IReadOnlyList<int> GrantableGeneralSpells(int classId,
        int charLevel, IReadOnlyList<SpellParser.SpellDef> spells,
        int raceId = 0)
    {
        var bit = classId is >= 1 and <= 63 ? 1UL << (classId - 1) : 0UL;
        var list = new List<int>();
        if (bit == 0) return list;
        foreach (var s in spells)
        {
            // Sorts raciaux (lv 45/60) : 50372 + couleur×10 + race×2 +
            // palier, race 0-4 = Humain/Zumi/Anuran/Ursun/Halfkin
            // (ordre prouvé par knowledge Racial_Skills × T_Spell). Un
            // sort d'une autre race n'est jamais octroyé.
            if (raceId > 0 && IsRacialSpell(s.Id, out var spellRace)
                && spellRace != raceId - 1) continue;
            // KB-07 : flag race générique (crafts/supports J5xx-J6xx).
            if (!RaceAllows(s.RaceFlag, raceId)) continue;
            // Général = bit de la classe présent + d'autres classes
            // couvertes (mask multi-bit), niveau perso suffisant.
            if ((s.ClassMask & bit) == 0 || s.ClassMask == bit) continue;
            if (s.RequiredLevel < 1 || s.RequiredLevel > charLevel)
                continue;
            list.Add(s.Id);
        }
        return list;
    }

    /// <summary>Sort racial (plage 50372-50421, 5 couleurs × 5 races ×
    /// 2 paliers) ; <paramref name="raceIndex"/> 0-4.</summary>
    public static bool IsRacialSpell(int spellId, out int raceIndex)
    {
        raceIndex = -1;
        if (spellId < 50372 || spellId > 50421) return false;
        raceIndex = (spellId - 50372) % 10 / 2;
        return true;
    }

    /// <summary>
    /// Garde-fou de cast (`SSpellData::CheckClass`) : mask vide ou
    /// contenant le bit de la classe active → OK. Un mask multi-bit
    /// est un skill « général » (couleur) octroyé par
    /// <see cref="GrantableGeneralSpells"/> pour TOUTE classe possédée,
    /// pas seulement l'active : il reste lançable après un switch de
    /// classe (50011 Deep Impact, mask sans le bit de la classe 5).
    /// Seul un class skill mono-classe d'une autre classe est refusé.
    /// </summary>
    public static bool CanCast(int classId, SpellParser.SpellDef spell)
    {
        if (spell.ClassMask == 0) return true;
        if (classId is < 1 or > 63) return true;
        if ((spell.ClassMask & (1UL << (classId - 1))) != 0) return true;
        return System.Numerics.BitOperations.PopCount(spell.ClassMask) > 1;
    }
}
