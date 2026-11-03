using Eden.Core.Data.Parsers;
using Eden.Core.Game;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Émission des blocs de stats du profil joueur — NCIDs 296
/// (`CNC_CZ_ClientAttrRefresh`), 492 (`CNC_CZ_ClientBaseAttrRefresh`)
/// et 493 (`CNC_CZ_ClientAttrLevelRefresh`).
///
/// Preuves (désasm zone `lapis::CCharacter::NotifyRefreshAttr`
/// @0x8415210 + Serialize @0x81e8940/0x81e8c20, capture zsniff
/// 17:22:35.925) :
///   492 = stats de BASE seules `[s16×5][i32 hp/mp][f32 walk/run]
///         [s16×3 timings]` — 32B ;
///   493 = stats flottantes `[f32×14]` — 56B ;
///   296 = stats TOTALES (base + équipement, ce que RefreshEQsAttr
///         accumule dans CCharacter+0x38c.. avant le calcul
///         CCharFormula::Str/Int/Dex/Spr/Luk) `[s16×14][i32×4]
///         [f32×2][i32][s16×8][f32×6]` — 96B.
/// Ordre des champs 296 : les 5 premières stats sont Str/Int/Dex/Spr/
/// Luk (les uVar7..uVar11 du ctor), puis 9 attrs élémentaires
/// (vtable+0xe8 args 8,9,6,4,5,7,1,2,3), i32 0, i32 vital, i32
/// MeleeDamage (P-ATK), i32 vtable+0x10c (M-ATK), f32 ×2, i32 vtable
/// +0x108 (DEF), 8 s16 (hit/dodge/crit…), 6 f32 (vitesses).
///
/// Formules natives (CCharFormula, K=0.01=DAT_087cc690,
/// 100.0=DAT_087cc680, 0.01d=DAT_087cc640) :
///   BasicX  = lvlStat × classPct / 100
///   TotalX  = (eqX + lvlStat) × (growth + classPct) / 100 + flat
///   MaxHP   = (eqHp + lvlHp) × (hpPct - 100 + proto + 100) / 100 + flat
/// où lvlStat = S_Level[niveau perso] cols5-9 (ordre Str,Dex,Int,
/// Spr,Luk — CLevelQuery::Deserialize), classPct = S_Class
/// StatPercents[4..8] = cols17-21 (Str,Dex,Int,Spr,Luk —
/// CClassQuery::Deserialize lit 7 shorts en cols15-21 :
/// hp%,mp%,str%,dex%,int%,spr%,luk%). Les termes proto (char+0x8c :
/// CKindData +0x178/+0x540/+0xd0/+0xcc) sont absents des données
/// joueur — traités comme 0.
/// </summary>
public static class ProfileStatEmitter
{
    /// <summary>Indices dans StatPercents (S_Class cols13-21) —
    /// mapping prouvé : pct[2]=hp%, [3]=mp%, [4]=str%, [5]=dex/agi%,
    /// [6]=int%, [7]=spr/wis%, [8]=luk%.</summary>
    private const int PctHp = 2, PctMp = 3, PctStr = 4, PctAgi = 5,
        PctInt = 6, PctWis = 7, PctLck = 8;

    /// <summary>Stats de base du perso (CCharFormula::Basic*) :
    /// S_Level[level] × S_Class% / 100, sans équipement.</summary>
    /// <returns>(str, int, agi, wis, lck, hp, mp) de base.</returns>
    public static (int Str, int Int, int Agi, int Wis, int Lck,
        int Hp, int Mp) ComputeBaseStats(ZoneServices s,
        int raceId, int classId, int level)
    {
        var lvl = s.GameData.Levels
            .FirstOrDefault(l => l.Level == Math.Max(level, 1));
        var cdef = s.GameData.Classes.FirstOrDefault(c =>
            c.Id == raceId * 100 + classId || c.Id == classId);
        if (lvl == null) return (10, 10, 10, 10, 10, 300, 150);
        var pct = cdef?.StatPercents ?? new int[9];
        int Pct(int i) => i < pct.Length && pct[i] > 0 ? pct[i] : 100;
        // Ordre S_Level : Str=col5, Dex=col6, Int=col7, Spr=col8,
        // Luk=col9 (toutes égales dans la table mais on garde la
        // correspondance native). Stats nommées jeu : Str, Int,
        // Agi(=Dex), Wis(=Spr), Lck(=Luk).
        int B(int lvlStat, int pctIdx) =>
            Math.Clamp(lvlStat * Pct(pctIdx) / 100, 0, 32000);
        return (
            B(lvl.Str, PctStr),
            B(lvl.Int, PctInt),
            B(lvl.Con, PctAgi),   // col6 = Dex natif → AGI
            B(lvl.Wis, PctWis),   // col8 = Spr natif → WIS
            B(lvl.Agi, PctLck),   // col9 = Luk natif → LCK
            Math.Max(1, lvl.MaxHp * Pct(PctHp) / 100),
            Math.Max(1, lvl.MaxMp * Pct(PctMp) / 100));
    }

    /// <summary>Vitals max natifs (CCharFormula::MaxHP @0x83f87e0 /
    /// MaxMP) : <c>(eqHp + lvlHp) × hpPct / 100 + flat +
    /// pdef / para562</c> — l'équipement est modulé par le % de classe
    /// comme le natif (SOMME eq+base multipliée, pas eq ajouté après) ;
    /// `pdef` = def naturelle (S_Level col4) + def équipement
    /// (GetPhysicoDefence), divisée par le param 562 (=20). Utilisé par
    /// le flow d'entrée et le level-up pour `st.PlayerMaxHp/Mp`.</summary>
    public static (int MaxHp, int MaxMp) ComputeVitals(ZoneServices s,
        int raceId, int classId, int level, int eqHp = 0,
        int eqMp = 0, int eqDef = 0)
    {
        var lvl = s.GameData.Levels
            .FirstOrDefault(l => l.Level == Math.Max(level, 1));
        var cdef = s.GameData.Classes.FirstOrDefault(c =>
            c.Id == raceId * 100 + classId || c.Id == classId);
        if (lvl == null) return (400, 200);
        var pct = cdef?.StatPercents ?? new int[9];
        int Pct(int i) => i < pct.Length && pct[i] > 0 ? pct[i] : 100;
        var defHp = (int)(((lvl?.NaturalDef ?? 0) + eqDef)
            / Math.Max(s.GameData.Parameters.Get(562, 20f), 1f));
        return (Math.Max(1, (lvl!.MaxHp + eqHp) * Pct(PctHp) / 100
                    + defHp),
                Math.Max(1, (lvl.MaxMp + eqMp) * Pct(PctMp) / 100));
    }

    /// <summary>Rejoue 492 + 493 + 296 pour le perso : stats de base
    /// (S_Level[niveau] × S_Class% — formule BasicX native) puis
    /// bonus de tout l'équipement porté (S_Item cols 37-50 via
    /// <see cref="EquipmentStatService"/>). Appelé après un équipement/
    /// déséquipement (NCID 8) et dans le burst de changement de classe.
    /// </summary>
    public static async Task<(int MaxHp, int MaxMp)> SendAsync(
        NetSession session, ZoneServices s, int charId, int raceId,
        int classId, int charLevel, CancellationToken token)
    {
        var equipped = await s.InventoryRepo
            .GetEquippedItemEntitiesAsync(charId);
        var eq = EquipmentStatService.Sum(equipped, s.GameData.Items,
            s.EquipTrain.ItemTrainAdds);
        // Bonus de vitesse actif — monture (RideStates → enchant →
        // S_RideStep SpeedPct), miroir du kinddata+0xa0 natif.
        float moveBonus = 0;
        if (s.RideStates.TryGetValue(charId, out var rideState))
            moveBonus = RideSpeedRules.EffectivePct(s.Enchants
                .GetRideInfo(rideState.EnchantId));
        var combat = BuildCombatSnapshot(s, raceId, classId, charLevel,
            eq, equipped, moveBonus);
        return await SendAsync(session, s, raceId, classId, charLevel,
            eq, combat, token, charId);
    }

    /// <summary>Variante tout-DB : charge le perso (race/classe et
    /// niveau personnage — l'index S_Level natif) ; utilisée par les
    /// handlers qui n'ont qu'un charId (NCID 8 équipement, clic
    /// droit). Retourne les vitals calculées pour que l'appelant
    /// synchronise l'état live (PlayerMaxHp/Mp de l'EntryState).</summary>
    public static async Task<(int MaxHp, int MaxMp)>
        SendForCharacterAsync(NetSession session, ZoneServices s,
            int charId, CancellationToken token)
    {
        var ch = await s.Characters.GetCharacterByIdAsync(charId);
        if (ch == null) return (0, 0);
        return await SendAsync(session, s, charId, ch.RaceId,
            ch.ClassId, Math.Max(ch.Level, 1), token);
    }

    /// <summary>Snapshot combat pour l'émission des stats — même
    /// agrégation que `ResolveWeaponAsync` (slots natifs 7 = main,
    /// 8 = off, 10 = distance/arme longue, kinds d'arme 0x11-0x21 |
    /// 0x3d-0x42) ; la licence deux-armes n'est pas connue ici → le
    /// dual est déduit de la classe twin 0x14 seule.</summary>
    private static Combat.CombatantSnapshot BuildCombatSnapshot(
        ZoneServices s, int raceId, int classId, int charLevel,
        EquipmentStats eq,
        IReadOnlyList<Eden.Core.Game.Inventory.ItemEntity>? equipped,
        float moveSpeedBonusPct = 0f)
    {
        var lvlRow = s.GameData.Levels
            .FirstOrDefault(l => l.Level == Math.Max(charLevel, 1));
        var cls = s.GameData.Classes.FirstOrDefault(c =>
            c.Id == raceId * 100 + classId || c.Id == classId);
        ItemParser.ItemDef? mainDef = null, offDef = null,
            longDef = null;
        static bool IsWeaponKind(int k) =>
            k is >= 0x11 and <= 0x21 or >= 0x3d and <= 0x42;
        if (equipped != null)
            foreach (var e in equipped)
            {
                var def = s.GameData.Items
                    .FirstOrDefault(i => i.Id == e.ItemId);
                if (def == null || !IsWeaponKind(def.Kind)) continue;
                switch (e.Slot)
                {
                    case 7: mainDef ??= def; break;
                    case 8: offDef ??= def; break;
                    case 10: longDef ??= def; break;
                }
            }
        if (mainDef == null && longDef != null)
        { mainDef = longDef; offDef = null; }
        return Combat.CombatantSnapshot.FromPlayer(charLevel, lvlRow,
            cls?.StatPercents, eq, mainDef, offDef,
            twoWeaponLicensed: false, classId, moveSpeedBonusPct);
    }

    /// <summary>Variante « entrée » du pipeline hors-boucle (Phase 2) :
    /// entités équipées déjà chargées (post-purge) + contribution du
    /// StatPipeline résolue sur les lignes préchargées — zéro requête.
    /// Appelée par les émissions différées de MigrationDone.</summary>
    public static async Task<(int MaxHp, int MaxMp)> SendAsync(
        NetSession session, ZoneServices s, int charId, int raceId,
        int classId, int charLevel,
        IReadOnlyList<Eden.Core.Game.Inventory.ItemEntity> equipped,
        StatContribution? prefetched, CancellationToken token)
    {
        var eq = EquipmentStatService.Sum(equipped, s.GameData.Items,
            s.EquipTrain.ItemTrainAdds);
        float moveBonus = 0;
        if (s.RideStates.TryGetValue(charId, out var rideState))
            moveBonus = RideSpeedRules.EffectivePct(s.Enchants
                .GetRideInfo(rideState.EnchantId));
        var combat = BuildCombatSnapshot(s, raceId, classId, charLevel,
            eq, equipped, moveBonus);
        return await SendAsync(session, s, raceId, classId, charLevel,
            eq, combat, token, charId, prefetched);
    }

    /// <summary>Variante qui reçoit l'agrégat déjà calculé (chemin
    /// classe où l'EntryState fournit le contexte) — sans entités
    /// d'équipement le snapshot n'a pas de slot d'arme : P-ATK/M-ATK/
    /// crit n'incluent pas le terme arme (dégradé documenté).</summary>
    /// <param name="charId">Si &gt; 0, les étapes enregistrées dans
    /// <c>s.StatPipeline</c> (titres, costumes, buffs) sont résolues et
    /// sommées à l'équipement.</param>
    /// <param name="prefetched">Contribution déjà résolue hors-boucle
    /// (pipeline d'entrée) — remplace l'appel
    /// <c>StatPipeline.ResolveAsync</c> quand fournie.</param>
    public static async Task<(int MaxHp, int MaxMp)> SendAsync(
        NetSession session, ZoneServices s, int raceId, int classId,
        int charLevel, EquipmentStats eq,
        Combat.CombatantSnapshot? combat = null,
        CancellationToken token = default, int charId = -1,
        StatContribution? prefetched = null)
    {
        var (bStr, bInt, bAgi, bWis, bLck, hpBase, mpBase) =
            ComputeBaseStats(s, raceId, classId, charLevel);
        var cdef = s.GameData.Classes.FirstOrDefault(c =>
            c.Id == raceId * 100 + classId || c.Id == classId);
        var pct = cdef?.StatPercents ?? new int[9];
        int Pct(int i) => i < pct.Length && pct[i] > 0 ? pct[i] : 100;

        // Ordre natif NotifyRefreshAttr @0x8415210 : ClientUpdateAttr
        // (vitals, émis par les callers) → 296 AttrRefresh (totales) →
        // 492 BaseAttrRefresh → 493 AttrLevelRefresh → UpdateAttr.
        // NOTRE ordre précédent (492→493→296) était inversé — le
        // client applique le bloc total en dernier pour l'affichage.

        // 296 ClientAttrRefresh — stats TOTALES = base + équipement.
        // Corps natif 96B (CNC_CZ_ClientAttrRefresh::Serialize
        // @0x81e8940 + NotifyRefreshAttr @0x8415210, ordre des champs
        // prouvé par les vfuncs de la vtable CCharacter 0x0888e5a0 +
        // golden zsniff_native_entry) :
        //   [s16×5 stats][s16×9 résists][i32 0][i32 MaxMP]
        //   [i32 MeleeDamage][i32 MagicDamage][f32 AttackSpeedRate
        //   (+0x160)][f32 MagicSpeedRate (+0x164)][i32 PhysicoDefence]
        //   [s16×8 ParryRate(+0x188),BlockRate(+0x184),HitRate(+0xf0),
        //          DodgeRate(+0xf4),CritRate(+0xf8),CritDmg(+0x100),
        //          MagicCritRate(+0xfc),MagicCritDmg(+0x104)]
        //   [f32×6 Movement,BuffAtkSpd,BuffCast,DefenceReduce,
        //          MovementRate,CriDmgReduce]
        // ⚠ le i32 vital (+0x24 de l'objet) = GetMaxMP (vtable+0xe4),
        // PAS MaxHP — l'ancien code y écrivait maxHp : chaque refresh
        // (buff appliqué à l'usage d'un skill, équipement…) affichait
        // MaxMP = MaxHP jusqu'au prochain 280 sel=0x12 (régén), bug
        // live confirmé. MaxMP natif = (eqMp + lvlMp) × mpPct/100 +
        // flat — même moule que ResolveStat (canaux pre/pct/post).
        // Les valeurs viennent des formules CCharFormula transposées
        // (NativeCombatFormulas) — plus aucun placeholder.
        var lvl = s.GameData.Levels
            .FirstOrDefault(l => l.Level == Math.Max(charLevel, 1));
        var combatParams = Combat.NativeCombatFormulas
            .ResolveParameters(s.GameData.Parameters);
        var combatStats = combat ?? BuildCombatSnapshot(s, raceId,
            classId, charLevel, eq, null);
        // Pipeline de stats (ordre demandé) : base → équipement →
        // titres/permanents → costumes → buffs/procs. L'équipement est
        // versé ici (agrégat déjà en main) ; les étapes enregistrées
        // dans s.StatPipeline viennent s'y sommer — la formule
        // TotalX native = (lvl + pre)×(pct + pctAdds)/100 + post.
        // charId > 0 : le stage « equipment » du StatPipeline porte
        // déjà core+HP/MP ; sans charId (appel anonyme) repli direct.
        var contrib = charId > 0
            ? new StatContribution()
            : StatContribution.FromEquipment(eq);
        if (charId > 0)
            contrib.Merge(prefetched
                ?? await s.StatPipeline.ResolveAsync(charId, token));
        // Canaux bonus (kinddata natif, CEC_Attr*) versés dans le
        // snapshot — même moulinet que le refresh live d'EntryState
        // (ZoneEntryFlow.Status → st.Combat.ApplyContribution).
        combatStats.ApplyContribution(contrib);
        var tStr = StatPipeline.ResolveStat(contrib,
            StatIndex.Str, lvl?.Str ?? 10, Pct(PctStr));
        var tInt = StatPipeline.ResolveStat(contrib,
            StatIndex.Int, lvl?.Int ?? 10, Pct(PctInt));
        var tAgi = StatPipeline.ResolveStat(contrib,
            StatIndex.Agi, lvl?.Con ?? 10, Pct(PctAgi));
        var tWis = StatPipeline.ResolveStat(contrib,
            StatIndex.Wis, lvl?.Wis ?? 10, Pct(PctWis));
        var tLck = StatPipeline.ResolveStat(contrib,
            StatIndex.Lck, lvl?.Agi ?? 10, Pct(PctLck));
        // Ordre natif des résists sur le fil : AttrResist appelé avec
        // les ids 8,9,6,4,5,7,1,2,3 (NotifyRefreshAttr) → resists[i−1].
        int[] resistOrder = { 8, 9, 6, 4, 5, 7, 1, 2, 3 };
        // Résistances wire = équipement + AttrResist des buffs
        // (kinddata+0x00..0x11 natif — sommé au même endroit que
        // SResistAttribute::AddResist).
        var res = (eq.Resists ?? new int[9])
            .Select((v, i) => v + contrib.ResistAdds[i]).ToArray();
        var pAtk = Combat.NativeCombatFormulas
            .MeleeDamage(combatStats, combatParams);
        var mAtk = Combat.NativeCombatFormulas
            .MagicDamage(combatStats, combatParams);
        var defence = Combat.NativeCombatFormulas
            .PhysicalDefence(combatStats);
        var attr = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAttrRefresh
        {
            Field1 = (short)tStr, Field2 = (short)tInt,
            Field3 = (short)tAgi, Field4 = (short)tWis,
            Field5 = (short)tLck,                   // s16 : totales
        };
        for (var i = 0; i < 9; i++)                 // s16 : résists (ordre natif)
        {
            var a = resistOrder[i];
            var v = (short)(a - 1 < res.Length ? res[a - 1] : 0);
            switch (i)
            {
                case 0: attr.Field6 = v; break;  case 1: attr.Field7 = v; break;
                case 2: attr.Field8 = v; break;  case 3: attr.Field9 = v; break;
                case 4: attr.Field10 = v; break; case 5: attr.Field11 = v; break;
                case 6: attr.Field12 = v; break; case 7: attr.Field13 = v; break;
                case 8: attr.Field14 = v; break;
            }
        }
        // Vitals max natifs — même moule que ComputeVitals :
        // MaxHP ajoute defHp ((NaturalDef + eqDef)/param562) après le
        // produit ; MaxMP = (lvl + pre)×(pct + pctAdds)/100 + post.
        var defHp = (int)(((lvl?.NaturalDef ?? 0) + eq.Def)
            / Math.Max(s.GameData.Parameters.Get(562, 20f), 1f));
        var tHp = Math.Max(1, StatPipeline.ResolveStat(contrib,
            StatIndex.Hp, lvl?.MaxHp ?? 0, Pct(PctHp)) + defHp);
        var tMp = Math.Max(1, StatPipeline.ResolveStat(contrib,
            StatIndex.Mp, lvl?.MaxMp ?? 0, Pct(PctMp)));
        attr.Field15 = 0;                           // i32 : 0
        attr.Field16 = tMp;                         // i32 : MaxMP (GetMaxMP)
        attr.Field17 = pAtk;                        // i32 : P-ATK
        attr.Field18 = mAtk;                        // i32 : M-ATK
        // f32 natifs : vfuncs +0x160 GetAttackSpeedRate /
        // +0x164 GetMagicSpeedRate (golden : 5.0/10.0 — ratio ×2 des
        // para321/322, jamais un niveau de crit). L'ancien ordre
        // (MCriLv, AttackSpeedLevelRate) affichait des valeurs
        // fausses dans les slots vitesses de la fiche.
        attr.Field19 = (float)Combat.NativeCombatFormulas
            .AttackSpeedLevelRate(combatStats, combatParams); // f32 : atk rate
        attr.Field20 = (float)Combat.NativeCombatFormulas
            .CastSpeedLevelRate(combatStats, combatParams); // f32 : cast rate
        attr.Field21 = defence;                     // i32 : DEF
        // Ordre natif des s16 (vfuncs vtable CCharacter, vptr=+8 :
        // +0x188 ParryRate, +0x184 BlockRate, +0xf0 HitRate,
        // +0xf4 DodgeRate, +0xf8 CritRate, +0x100 CritDmg,
        // +0xfc MagicCritRate, +0x104 MagicCritDmg — prouvé par les
        // goldens : #6/#8 constants à 150 = para311/314). L'ancien
        // ordre mettait MagicCriticalRate en #6 → le client affichait
        // le M-Crit (×100, ~900) comme « CRIT DMG 9.00 » — bug live.
        var ratingFields = new[] {
            Combat.NativeCombatFormulas.ParryRate(combatStats, combatParams),
            Combat.NativeCombatFormulas.BlockRate(combatStats,
                combatParams),
            Combat.NativeCombatFormulas.PhysicalHitRate(combatStats,
                combatParams),
            Combat.NativeCombatFormulas.PhysicalDodgeRate(combatStats,
                combatParams),
            Combat.NativeCombatFormulas.PhysicalCriticalRate(combatStats,
                combatParams),
            Combat.NativeCombatFormulas.PhysicalCriticalDamage(combatStats,
                combatParams),
            Combat.NativeCombatFormulas.MagicCriticalRate(combatStats,
                combatParams),
            Combat.NativeCombatFormulas.MagicCriticalDamage(combatStats,
                combatParams),
        };
        for (var i = 0; i < 8; i++)
        {
            var v = (short)Math.Clamp(ratingFields[i], 0, 32000);
            switch (i)                        // s16 : ratings natifs
            {
                case 0: attr.Field22 = v; break; case 1: attr.Field23 = v; break;
                case 2: attr.Field24 = v; break; case 3: attr.Field25 = v; break;
                case 4: attr.Field26 = v; break; case 5: attr.Field27 = v; break;
                case 6: attr.Field28 = v; break; case 7: attr.Field29 = v; break;
            }
        }
        // Movement — custom : base × (1 + (monture% +
        // AGI×AgiToMoveSpeed)/100) capé MoveSpeedCapPct
        // (spec live : AGI→move speed 0.08/pt, cap 300%).
        var floatFields = new float[] {
            Combat.NativeCombatFormulas.Movement(combatStats),
            // BuffAttackSpeedRate / BuffCastTimeRate — les % de
            // vitesse d'attaque et de cast venant des buffs/talents
            // (cmd 2056/2058/2068 → AttackSpeedRatePercent,
            // 2069 → CastSpeedRatePercent). Laissés à 0 → le talent
            // cast-spd n'apparaissait jamais sur le profil.
            (float)combatStats.BonusAttackSpeedPercent,
            (float)combatStats.BonusCastSpeedPercent,
            Combat.NativeCombatFormulas.DefenceReduce(combatStats,
                combatParams),           // réduction de dégâts %
            // MovementRate — bonus % affiché ((mult−1)×100) :
            // monture + AGI custom, natif (kinddata+0xa0−1)×100.
            Combat.NativeCombatFormulas.MovementRate(combatStats),
            Combat.NativeCombatFormulas.CriDmgReduce(combatStats),
        };
        attr.Field30 = floatFields[0]; attr.Field31 = floatFields[1];
        attr.Field32 = floatFields[2]; attr.Field33 = floatFields[3];
        attr.Field34 = floatFields[4]; attr.Field35 = floatFields[5];
        if (NetDebug.Enabled)
            EdenLog.Write($"[Profile296] char={charId} " +
                $"total=({tStr},{tInt},{tAgi},{tWis},{tLck}) " +
                $"ratings=[{string.Join(',', ratingFields)}] " +
                $"floats=[{string.Join(',', floatFields)}]");
        // Vitals max AVANT le 296 — ordre natif NotifyRefreshAttr :
        // UpdateAttr sel=0x11 (MaxHP i64) puis sel=0x12 (MaxMP) au
        // client. Indispensable : le 296 ne porte que MaxMP — un
        // buff/debuff de MaxHP n'était JAMAIS visible côté client
        // (et un buff MaxMP n'arrivait qu'ici).
        await session.SendAsync(new Eden.Core.Networking.Messages
            .Generated.CNC_CZ_ClientUpdateAttr
        {
            Field1 = charId > 0 ? charId : 0,     // i32 : handle self
            Field2 = 0x11,                        // s16 : attr MaxHP
            Field3 = tHp, Field4 = tHp,           // p1=i32, p2=i64
        }, token);
        await session.SendAsync(new Eden.Core.Networking.Messages
            .Generated.CNC_CZ_ClientUpdateAttr
        {
            Field1 = charId > 0 ? charId : 0,
            Field2 = 0x12,                        // s16 : attr MaxMP
            Field3 = tMp, Field4 = 0L,            // p1=i32, p2=0
        }, token);
        await session.SendAsync(attr, token);

        // 492 ClientBaseAttrRefresh — stats de base SEULES (le natif
        // envoie BasicStr/BasicInt/… = sans équipement), émis après le
        // bloc total quand param_1 (refresh complet). Ordre natif :
        // Str, Int, Dex, Spr, Luk.
        await session.SendAsync(new Eden.Core.Networking.Messages
            .Generated.CNC_CZ_ClientBaseAttrRefresh
        {
            Field1 = (short)bStr, Field2 = (short)bInt,
            Field3 = (short)bAgi, Field4 = (short)bWis,
            Field5 = (short)bLck,                   // s16 : stats de base
            Field6 = hpBase, Field7 = mpBase,       // i32 : hp/mp base
            Field8 = 7.5f, Field9 = 15f,            // f32 : marche/course
            Field10 = 361, Field11 = 150, Field12 = 150, // s16 : timings
        }, token);

        // 493 ClientAttrLevelRefresh `[f32×14]` — paliers de stats,
        // ordre natif prouvé par la vtable CCharacter @0x0888e5a8 :
        // [HitLv(+0x154), CriLv(+0x14c), MCriLv(+0x15c), DodgeLv(+0x134),
        //  ParryLv(+0x180), BlockLv(+0x178), BaseHitLv(+0x150),
        //  BaseCriLv(+0x148), BaseMCriLv(+0x158), BaseDodgeLv(+0x130),
        //  BaseParryLv(+0x17c), BaseBlockLv(+0x174),
        //  AttackSpeedLv(+0x13c), MagicSpeedLv(+0x144)].
        // Le field2 (CriLv) est le « taux de crit » flat de la fiche —
        // c'est lui que le proc 2048|360 doit faire monter (le 296 ne
        // porte que les taux % ×100 ; l'ancien placeholder ne bougeait
        // jamais → flat figé à 110 malgré le proc).
        var lvFields = new float[] {
            Combat.NativeCombatFormulas.HitLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.CriLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.MCriLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.DodgeLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.ParryLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.BlockLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.BaseHitLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.BaseCriLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.BaseMCriLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.BaseDodgeLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.BaseParryLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.BaseBlockLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.AttackSpeedLv(combatStats, combatParams),
            Combat.NativeCombatFormulas.MagicSpeedLv(combatStats, combatParams),
        };
        await session.SendAsync(new Eden.Core.Networking.Messages
            .Generated.CNC_CZ_ClientAttrLevelRefresh
        {
            Field1 = lvFields[0], Field2 = lvFields[1],
            Field3 = lvFields[2], Field4 = lvFields[3],
            Field5 = lvFields[4], Field6 = lvFields[5],
            Field7 = lvFields[6], Field8 = lvFields[7],
            Field9 = lvFields[8], Field10 = lvFields[9],
            Field11 = lvFields[10], Field12 = lvFields[11],
            Field13 = lvFields[12], Field14 = lvFields[13],
        }, token);
        return (tHp, tMp);
    }
}
