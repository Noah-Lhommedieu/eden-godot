using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game;
using Eden.Core.Game.Party;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Combat;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    // ------------------------------------------------------------------
    // FlagModify 302 — wire exact (capture zsniff native, corps 9B) :
    //   [i32 handle][u16 flags16][u8 sel][u8 op][u8 val]
    // flags16 = bits immédiats — LITTLE-ENDIAN sur le fil :
    //   swing joueur = 0x2000 (capture `…<char> 00 20 00 01 1e`), 0 sinon ;
    // sel = champ d'état modifié (0 = phase attaque, 4 = combat,
    //   0x14 = phase d'attaque du mob) ; op = 1 set / 0 clear ;
    // val = actionId — compteur d'actions PAR entité, NOUVEL id à chaque
    // action sinon le client ne rejoue pas l'animation.
    //
    // Capture — joueur attaquant (handle = charId) :
    //   0x2000 sel=0 op=1 val=N → 0x2000 sel=4 op=1 val=N →
    //   0x0000 sel=4 op=1 val=N (répété ; N constant pendant l'engagement)
    //   sortie : 0x0000 sel=0 op=1 val=N+1
    // Mob : aggro 0000 sel=4 op=0 val=1 ; phases 0000 sel=14 op=0 val=3..6
    // ------------------------------------------------------------------
    // sel = bits16-23 du mot d'état entity+0x3a émis entier dans le 302
    // (NodifyFlagChange @0x84e3c00 : Serialize = [u32 handle][u32 mot]
    // [u8 seq]). sel=4 = bit18 « bataille », sel=0x14 = bit20 « phase
    // d'attaque mob ». sel=1 (bit16) n'apparaît dans AUCUNE capture —
    // état fantôme qui gelait le client (personnage+caméra figés).
    private const byte SEL_COMBAT = 0x04, SEL_PHASE = 0x14;

    /// <summary>Émet le 302 — chaque émission porte le mot
    /// <c>char+0x3a</c> ENTIER (le client écrase actor+0x3c avec :
    /// <c>NodifyFlagChange</c> natif @0x084e3c00). Le bit9 « mounted »
    /// (0x0200, state 9) est donc <b>re-fusionné ici</b> quand le
    /// handle est un perso monté : un cavalier de monture de combat
    /// qui enchaîne les 302 combat garderait sinon son bit ride
    /// effacé à la première émission → le client perdrait les
    /// records de strafe monture (player+0x3c bit9).</summary>
    private Task SendFlagAsync(NetSession session, uint handle,
        ushort f16, byte sel, byte op, byte actionId,
        CancellationToken token)
    {
        // handle = charId pour les persos ; les handles mobs
        // (0xFFFFAxxx → négatif en int) ne matchent jamais RideStates.
        if (_services.RideStates.ContainsKey((int)handle))
            f16 |= (ushort)Codecs.StateFlagWord.Mounted;
        return session.SendAsync(new CNC_CZ_ClientFlagModify
        {
            Handle = handle, Flags = f16, Selector = sel,
            Operation = op, ActionId = actionId,
        }, token);
    }

    // Profils capturés (f16, sel, op) — val = actionId fourni à l'appel.
    private Task SendPlayerSwingBeginAsync(NetSession session,
        uint charId, byte act, CancellationToken token) =>
        SendFlagAsync(session, charId, 0x2000, 0, 1, act, token);

    /// <summary>272 ClientMeleeAttack : [u32 atk][u32 cible][i32 dmg]
    /// [i32 type][i32 motionId|-1]. Types observés : 1/3 joueur→mob,
    /// 1 mob→joueur. field5 = kfId d'attaque rendu par
    /// CKfm::GetAttackID (désasm 0x80dfd90/0x80e0b90/0x80e1010 —
    /// `param_2 = triplet[0]` du pool d'attaques du KFM, défaut 0x3fd) :
    /// capture joueur→mob 0x58d = kf 1421 (attack12 du KFM perso),
    /// mob→joueur 0x3fd/0x3fe = kf 1021/1022 (attack01/02 du m025).
    /// -1 (0xFFFFFFFF) = twin/coup fatal sans motion. C'est CE champ qui
    /// déclenche l'animation de coup côté client — y mettre des HP
    /// supprimait toute anim d'attaque.</summary>
    private Task SendMeleeAsync(NetSession session, uint attacker,
        uint target, int dmg, int type, int motionId, CancellationToken token) =>
        // 272 CNC_CZ_ClientMeleeAttack [u32 attaquant][u32 cible]
        // [i32 dmg][i32 type][i32 motionId] — codec généré.
        session.SendAsync(new CNC_CZ_ClientMeleeAttack
        {
            Field1 = attacker, Field2 = target, Field3 = dmg,
            Field4 = type, Field5 = motionId
        }, token);

    /// <summary>Pioche un kfId d'attaque — `CCharKfm::GetAttackID`
    /// natif @0x80e0b90. Monture de combat (char+0x3b bit1 =
    /// riding, char+0x824 = arg4 du 2134 ≠ 0, KFM `r{key}` chargé en
    /// char+0xd60) : le kfId est pioché dans le KFM <b>de la
    /// monture</b> — natif : `0x3fe - rand()&amp;1` (1021/1022 =
    /// attack01/02 du r253), arc kind 0x1c → 0x3ff (1023), gun 0x1d
    /// → 0x400 (1024). Un kfId d'arme n'existe pas dans la table de
    /// la monture → le client ne jouait aucune anim à dos.
    /// À pied : pool de l'arme équipée, `random() % count`.</summary>
    /// <summary>Monture de combat active — condition ride native
    /// (char+0x3b bit2 && char+0x824 ≠ 0) modélisée côté serveur par
    /// RideStates + enchant de monture combattante (RideType≠0 ou
    /// buffs). Sert au choix du KFM monture pour les motions
    /// d'attaque/blast.</summary>
    private bool RidingCombatMount(EntryState st) =>
        _services.RideStates.TryGetValue((int)st.CharId, out var ride)
        && _services.Enchants.GetRideInfo(ride.EnchantId) is
        { } ri && (ri.RideType != 0 || ri.CombatBuffs.Count > 0);

    private int NextPlayerAttackKf(EntryState st)
    {
        if (RidingCombatMount(st))
        {
            return st.WeaponKind switch
            {
                0x1c => 0x3ff,   // arc → attack03 de la monture
                0x1d => 0x400,   // gun → attack04
                _ => 0x3fe - Random.Shared.Next(2), // 1021/1022
            };
        }
        return st.WeaponKfIds.Count > 0
            ? st.WeaponKfIds[Random.Shared.Next(st.WeaponKfIds.Count)]
            : MotionParser.DefaultAttackKfId;
    }

    /// <summary>Résout l'arme équipée du joueur → pool de kfIds d'attaque
    /// + portée. L'équipement (table equipment, loc 10 = slot arme) donne
    /// l'itemId ; S_Item col8 = kind d'arme mappé vers le bucket KFM
    /// natif ; col48 × 1.5 = portée (CCharFormula::AttackRange) + rayons
    /// (~1) + tolérance native 1.5 + marge 3 pour ne jamais rejeter un
    /// coup que le client juge en portée (sa poursuite s'arrête à SA
    /// portée — un rejet serveur casserait le combat).
    /// Aucune arme trouvée → pool désarmé (1121/1122/2121) + 7.5u.</summary>
    /// <param name="prefetched">Paires (loc, itemId) déjà chargées —
    /// pipeline d'entrée hors-boucle (Phase 2) : évite la requête
    /// <c>GetEquippedItemsAsync</c>.</param>
    private async Task ResolveWeaponAsync(EntryState st,
        IReadOnlyList<(int Loc, int ItemId)>? prefetched = null)
    {
        try
        {
            var equipped = prefetched ?? await _services.InventoryRepo
                .GetEquippedItemsAsync((int)st.CharId);
            // Kinds d'arme S_Item : 0x11-0x21 mêlée/archer/mage +
            // 0x3d-0x42 armes awakened (CItemData+0x20, buckets KFM).
            // Le bouclier (kind 0x22/34) n'est PAS une arme.
            static bool IsWeaponKind(int k) =>
                k is >= 0x11 and <= 0x21 or >= 0x3d and <= 0x42;
            // GetWeaponItem natif (@0x840cea0) : main=loc7, off=loc8,
            // arme longue=loc10. EquipType 9/35 est remappé à loc7/10
            // par CItemFactory::Equip @0x80b5810 avant stockage. Une
            // paire 1H (7+8) → pools DUAL de CCharKfm (matrice 3×3).
            // Ordre DB non garanti : on collecte par rôle, puis on
            // résout par priorité; les EquipType bruts 9/35 ne sont
            // jamais des slots du CEquipment natif.
            int mainKind = 0, offKind = 0, longKind = 0;
            int mainRange = 0, longRange = 0, mainItem = 0, longItem = 0;
            ItemParser.ItemDef? mainDef = null, offDef = null,
                longDef = null;
            // Agrégat équipement complet (char+0x38c..+0x3e2 natifs) —
            // chaque ItemDef équipé contribue ses colonnes S_Item,
            // armes incluses (le double comptage patk/hit de l'arme est
            // natif : RefreshEQAttr + terme arme des formules).
            var eq = new EquipmentStats();
            var div = GameBalance.CoreStatDivisor;
            var procs = new List<int>();
            foreach (var (loc, itemId) in equipped)
            {
                var def = _services.GameData.Items
                    .FirstOrDefault(i => i.Id == itemId);
                if (def == null) continue;
                // useEnchant (col23) source de proc : déclencheur
                // (S_Enchant col41 ≠ 0 → GetProc) OU MultiStroke
                // passif en bloc non déclenché (GetAttackCommands —
                // les « +N % double hit » d'équipement ont souvent
                // col40/41 vides). → CEnchant équipement natif
                // (RefreshEQEffect → EffectToLife flag 0x1000) qui
                // vote dans OnAttackEnchant/OnBeAttackEnchant/…
                if (def.UseEnchantId > 0 &&
                    (_services.Enchants.GetProc(def.UseEnchantId)
                        != null ||
                     _services.Enchants.GetAttackCommands(
                        def.UseEnchantId).Count > 0))
                    procs.Add(def.UseEnchantId);
                eq = eq.Add(new EquipmentStats(
                    def.BonusHp, def.BonusMp,
                    def.BonusStr * div, def.BonusAgi * div,
                    def.BonusInt * div, def.BonusWis * div,
                    def.BonusLck * div,
                    def.PhysicalAttack, def.MagicAttack, def.Defence,
                    def.Hit, def.Dodge, def.Critical, def.CriticalDamage,
                    def.MagicCritical, def.MagicCriticalDamage,
                    def.Resistances));
                if (!IsWeaponKind(def.Kind)) continue;
                // Slots CEquipment natifs : 7=main, 8=off-hand,
                // 10=arme longue/distance.
                switch (loc)
                {
                    case 7:
                        if (mainKind == 0)
                        { mainKind = def.Kind; mainItem = itemId;
                          mainRange = (int)def.AttackRange;
                          mainDef = def; }
                        break;
                    case 8:
                        if (offKind == 0)
                        { offKind = def.Kind; offDef = def; }
                        break;
                    case 10:
                        if (longKind == 0)
                        { longKind = def.Kind; longItem = itemId;
                          longRange = (int)def.AttackRange;
                          longDef = def; }
                        break;
                }
            }
            // La main 1H a priorité sur le slot long; le natif consulte
            // les emplacements 7/8 puis 10 selon l'arme active.
            if (mainKind == 0 && longKind != 0)
            {
                mainKind = longKind; mainItem = longItem;
                mainRange = longRange; mainDef = longDef;
                offKind = 0; offDef = null;
            }
            st.WeaponItemId = mainItem;
            st.WeaponKind = mainKind;
            st.WeaponOffKind = offKind;
            st.WeaponKfIds = MotionParser.PlayerAttackKfIds(mainKind,
                offKind);
            st.AttackRange = mainKind != 0
                ? mainRange * 1.5f + 3f : 7.5f;
            st.ProcEnchantIds = procs.Count > 0 ? procs : null;
            st.Combat = BuildPlayerSnapshot(st, eq, mainDef, offDef);
            st.Combat.BonusDodgePercent = _services.Licenses
                .ProvideStats((int)st.CharId).DodgePercent;
            // Canaux Bonus* de TOUS les stages du StatPipeline
            // (gems/fortify/costumes/talents…) : sans ce versement
            // un snapshot reconstruit à l'équipement/inlay perdait
            // ces bonus jusqu'au prochain refresh de statut.
            try
            {
                st.Combat.ApplyContribution(await _services.StatPipeline
                    .ResolveAsync((int)st.CharId));
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Zone] stage bonus -> combat FAIL " +
                    $"char={st.CharId} : {ex.Message}");
            }
            // Traçage du pipeline de procs (JS2) : équipement lu,
            // useEnchant col23 de chaque pièce et ids retenus — un
            // pipeline « procs jamais déclenchés » se diagnostique ici
            // (équipement vide, def introuvable, enchant sans
            // déclencheur col41/col40).
            EdenLog.Write($"[Zone] weapon resolve char={st.CharId} " +
                $"eq=[{string.Join(',', equipped.Select(e =>
                    $"{e.Loc}:{e.ItemId}"))}] " +
                $"procs=[{string.Join(',', procs)}] " +
                $"weapon={st.WeaponItemId}/k{st.WeaponKind}");
            return;
        }
        catch (Exception ex)
        {
            // Ne JAMAIS rester muet : un échec DB ici laisse l'arme
            // ET les procs non résolus pour toute la session
            // (WeaponResolved reste true — symptôme « aucun proc »).
            EdenLog.Write($"[Zone] weapon resolve FAIL " +
                $"char={st.CharId} : {ex.Message}");
        }
        finally { st.WeaponResolved = true; }
    }

    /// <summary>Jeu de paramètres S_Parameter résolu à la première
    /// attaque (formules natives — ids `CFormula::AllFormulaParas`).</summary>
    private NativeCombatFormulas.Params _combatParams;
    private bool _combatParamsInit;

    /// <summary>Paramètres de combat S_Parameter (lazy — GameData est
    /// prêt avant le premier swing).</summary>
    private NativeCombatFormulas.Params CombatParams()
    {
        if (!_combatParamsInit)
        {
            _combatParams = NativeCombatFormulas.ResolveParameters(
                _services.GameData.Parameters);
            _combatParamsInit = true;
        }
        return _combatParams;
    }


    /// <summary>Snapshot combat du joueur — `st.Combat` construit par
    /// ResolveWeaponAsync ; si la résolution a échoué (DB illisible) on
    /// bâtit un repli S_Level+S_Class seul (équipement vide, désarmé)
    /// pour que les formules natives aient toujours un attaquant.</summary>
    private CombatantSnapshot PlayerSnapshot(EntryState st)
    {
        if (st.Combat != null) return st.Combat;
        st.Combat = BuildPlayerSnapshot(
            st, new EquipmentStats(), null, null);
        st.Combat.BonusDodgePercent = _services.Licenses
            .ProvideStats((int)st.CharId).DodgePercent;
        return st.Combat;
    }

    /// <summary>Niveau d'un sort pour ce joueur — `player_spells.order`
    /// (palier S_SpellReference) ; 1 si le sort est hors table
    /// (référence introuvable → coef ×1, natif SSpellData+0x30
    /// défaut).</summary>
    private async Task<int> PlayerSpellLevelAsync(EntryState st,
        int spellId)
    {
        // Cache par perso — la requête player_spells complète ne part
        // qu'une fois par session de zone (spell_id est s16 signé en
        // DB : 50017 → -15535).
        if (st.SpellLevels == null)
        {
            try
            {
                var rows = await _services.Classes
                    .GetPlayerSpellsAsync((int)st.CharId);
                st.SpellLevels = new Dictionary<int, int>(rows.Count);
                foreach (var r in rows)
                    st.SpellLevels[r.Spell] = r.Level;
            }
            catch { return 1; }
        }
        return st.SpellLevels.TryGetValue(
            unchecked((short)spellId), out var lvl)
            ? Math.Max(lvl, 1) : 1;
    }

    /// <summary>Coût MP natif — `CCharFormula::CostMp` @0x83f44d0 :
    /// `ref+4 (S_SpellReference col1, palier du niveau du sort)
    /// × base (S_Spell col32 → SSpellData+0xbc) × 0.01`, arrondi.
    /// La modulation BuffCostRate du lanceur (char+0x8c+0xb4/+0xb0)
    /// n'est pas modélisée ; le flat `CostMPPlus` (cmd 2015, talents)
    /// s'applique en plus — négatif = réduction.
    /// 0 si le sort n'a pas de base MP ou si le palier manque.</summary>
    private int SpellMpCost(SpellParser.SpellDef def, int level,
        int flat = 0)
    {
        if (def.MpCost <= 0) return 0;
        var lc = _services.Spells.GetUpgradeCost(Math.Max(level, 1));
        var coef = lc?.MpCoef ?? 0;
        return Math.Max(0, (int)Math.Round(
            ((coef >> 16) + (coef & 0xffff)) * def.MpCost * 0.01)
            + flat);
    }

    /// <summary>Coefficient de palier du sort — `SSpellReference`
    /// (S_SpellReference col2 physique / col3 magique, lue en % par les
    /// formules natives `power×coef/100`). Canal choisi par
    /// `SpellHitKindOf` ; 100 si le palier est hors table (×1).</summary>
    private int SpellRefCoef(SpellParser.SpellDef def, int level)
    {
        var lc = _services.Spells.GetUpgradeCost(Math.Max(level, 1));
        return NativeCombatFormulas.SpellHitKindOf(def) switch
        {
            // Canaux du palier natif : phys→col2 (+8), heal→col4 (+0x10)
            // (CalculateMagicSkillHeal @0x83f9310 lit ref+0x10),
            // magique→col3 (+0xc).
            SpellHitKind.Physical => lc?.PhysicalCoef ?? 100,
            SpellHitKind.Restore => lc?.HealCoef ?? 100,
            _ => lc?.MagicCoef ?? 100,
        };
    }

    /// <summary>Construit le `CombatantSnapshot` joueur : stats brutes
    /// S_Level (cols5-9 → StrTotal..LckTotal — les % classe s'appliquent
    /// dans les formules, comme char+0x354.. natif), défense naturelle
    /// col4 (char+0x370), pourcentages S_Class cols17-21
    /// (char+0x338..0x348), agrégat équipement complet (char+0x38c..
    /// 0x3e2 — armes incluses, double comptage natif) et ItemDefs des
    /// deux mains. `DualWield` = off-hand arme ET (classe twin 0x14 ou
    /// licence deux-armes `st.TwoWeaponUnlocked` — param_1[0x47c]==0x14
    /// natif).</summary>
    private CombatantSnapshot BuildPlayerSnapshot(EntryState st,
        EquipmentStats eq, ItemParser.ItemDef? mainDef,
        ItemParser.ItemDef? offDef)
    {
        var lvlRow = _services.GameData.Levels
            .FirstOrDefault(l => l.Level == st.Level);
        var cls = _services.GameData.Classes
            .FirstOrDefault(c => c.Id == st.RaceId * 100 + st.ClassId
                || c.Id == st.ClassId);
        // Bonus de vitesse actif — monture (RideStates → enchant →
        // SpeedPct S_RideStep) = le kinddata+0xa0 natif.
        float moveBonus = 0;
        if (_services.RideStates.TryGetValue((int)st.CharId,
                out var rs))
            moveBonus = _services.Enchants
                .GetRideInfo(rs.EnchantId)?.SpeedPct ?? 0;
        return CombatantSnapshot.FromPlayer(st.Level, lvlRow,
            cls?.StatPercents, eq, mainDef, offDef,
            st.TwoWeaponUnlocked, st.ClassId, moveBonus);
    }



    /// <summary>326 ClientMovePrediction — format désasm du ctor
    /// (`CPlayerFactory::MoveSelf`) : positions ×16 en u16
    /// (`[u32 handle][u16 curX×16][u16 curZ×16][u8 flag][u16 facing
    /// half-float|hasDst]`, puis `[u16 dstX×16][u16 dstZ×16]` quand la
    /// destination diffère — bit0 du champ facing posé).
    /// flag=0x07 observé sur les mobs ; facing = direction du
    /// déplacement pour une entité IA (NaN → atan2(dst-cur)).
    /// C'est LE paquet qui fait marcher visiblement les entités — le
    /// 267 seul ne joue pas l'anim de course côté client.</summary>
    private static Task SendMovePredictionAsync(NetSession session,
        uint handle, float curX, float curZ, float dstX, float dstZ,
        CancellationToken token, float facing = float.NaN)
    {
        var moving = Math.Abs(dstX - curX) > 0.01f ||
            Math.Abs(dstZ - curZ) > 0.01f;
        if (float.IsNaN(facing))
            facing = moving ? NativeDir(dstX - curX, dstZ - curZ) : 0f;
        return session.SendAsync(new CNC_CZ_ClientMovePrediction
        {
            Handle = (int)handle,                       // i32 : handle
            CurX = (ushort)(int)(curX * 16f),           // u16 : cur ×16
            CurZ = (ushort)(int)(curZ * 16f),           // u16 : cur ×16
            Flag = 0x07,                                // u8  : flag (mob)
            Facing = FacingToWire(facing, moving),      // u16 : half|dst
            DstX = moving ? (ushort)(int)(dstX * 16f) : null,
            DstZ = moving ? (ushort)(int)(dstZ * 16f) : null,
        }, token);
    }

    /// <summary>280 ClientUpdateAttr : [handle][s16 attrId][i32 p1][i64 p2].
    /// Sémantique prouvée par les émissions natives (désasm) :
    ///   attr 0    = HP courant broadcast — p1=low32(cur), p2=i64(cur)
    ///               (ctor @0x81009xx : [3]=+0x60lo [4]=+0x60lo [5]=+0x64)
    ///   attr 1    = MP courant broadcast — p1=cur, p2=0 (regen @0x8100xxx)
    ///   attr 0x11 = MaxHP self — p1=low32(max), p2=i64(max)
    ///               (+0xe0 = GetMaxHP i64, comparé à +0x60 dans la regen)
    ///   attr 0x12 = MaxMP self — p1=max (+0xe4 = GetMaxMP), p2=0
    ///   attr 0x13 = flag combat (100).
    /// ERREUR HISTORIQUE corrigée : on envoyait cur dans 0x11/0x12 —
    /// le client affichait le "max" qui bougeait au cast/regen.</summary>
    private static Task SendAttrAsync(NetSession session, uint handle, short attr,
        int cur, int max, CancellationToken token) =>
        // 280 CNC_CZ_ClientUpdateAttr [i32 handle][s16 attr]
        // [i32 valeur][i64 valeur-large] — le i32 max + i32 0
        // d'avant = le i64 natif (LE). Codec généré.
        session.SendAsync(new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)handle, Field2 = attr, Field3 = cur,
            Field4 = max
        }, token);

    /// <summary>Update vitals HP natif : attr 0 (cur : p1=low32, p2=i64)
    /// puis attr 0x11 (max : p1=low32, p2=i64) — deux paquets comme le
    /// natif qui sépare courant et maximum.</summary>
    private static async Task SendVitalHpAsync(NetSession session,
        uint handle, int cur, int max, CancellationToken token)
    {
        await session.SendAsync(new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)handle, Field2 = 0,         // s16 : attr HP cur
            Field3 = cur, Field4 = cur,             // p1=i32, p2=i64
        }, token);
        await session.SendAsync(new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)handle, Field2 = 0x11,      // s16 : attr HP max
            Field3 = max, Field4 = max,
        }, token);
    }

    /// <summary>Update vitals MP natif : attr 1 (cur : p1, p2=0) puis
    /// attr 0x12 (max : p1, p2=0).</summary>
    private static async Task SendVitalMpAsync(NetSession session,
        uint handle, int cur, int max, CancellationToken token)
    {
        await session.SendAsync(new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)handle, Field2 = 1,         // s16 : attr MP cur
            Field3 = cur, Field4 = 0L,
        }, token);
        await session.SendAsync(new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)handle, Field2 = 0x12,      // s16 : attr MP max
            Field3 = max, Field4 = 0L,
        }, token);
    }

    /// <summary>Swing joueur — capture : `0020 sel=4 val=N` (engagé+
    /// attaque) puis `0000 sel=4 val=N` (soutenu), même actionId pour
    /// toute la chaîne d'attaque.</summary>
    private async Task SendPlayerSwingAsync(NetSession session,
        uint charId, byte act, CancellationToken token)
    {
        await SendFlagAsync(session, charId, 0x2000, SEL_COMBAT, 1, act, token);
        await SendFlagAsync(session, charId, 0x0000, SEL_COMBAT, 1, act, token);
    }

    /// <summary>Sortie de combat — capture : `0000 sel=0 op=1 val=N+1`
    /// (17:23:02.490 `2e01 <char> 0000 00 01 1f` à la mort du mob,
    /// idem 17:23:11.788 / 17:23:17.601 — sel=0, nouvel actionId).
    /// Le client repasse en idle — sans ça il reste figé en combat.</summary>
    private Task SendCombatExitAsync(NetSession session,
        uint charId, byte act, CancellationToken token) =>
        SendFlagAsync(session, charId, 0x0000, 0, 1, act, token);

    /// <summary>Entrée en combat — capture : `0020 sel=0 val=N` (début
    /// d'action attaque) suivi du swing engagé.</summary>
    private async Task SendCombatEnterAsync(NetSession session,
        uint charId, byte act, CancellationToken token)
    {
        await SendPlayerSwingBeginAsync(session, charId, act, token);
        await SendPlayerSwingAsync(session, charId, act, token);
    }


    /// <summary>Évènement différé d'un coup mêlée (CEventDelayDamage
    /// natif — temps `hit` de la motion d'attaque + vol éventuel,
    /// schedulé sur la boucle de zone) :
    /// `280 mob attr0(hp)+attr13` puis le twin `272 hpLeft=-1` même
    /// type que le coup réel (capture 17:23:00.319).</summary>
    private async Task MeleeHitDelayedAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int dmg, int type,
        int generation)
    {
        try
        {
            if (!st.Migrated ||
                !mob.IsCombatTarget(generation, (int)st.CharId))
                return;
            await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
                CancellationToken.None);
            await SendAttrAsync(session, mob.Handle, 0x13, mob.Patience, 0,
                CancellationToken.None);
            await SendMeleeAsync(session, st.CharId, mob.Handle, dmg,
                type, -1, CancellationToken.None);
        }
        catch { /* session morte */ }
    }

    /// <summary>Bloc différé du premier contact : 271 acquisition cible +
    /// 302 mob aggro + attrs + twin + 444 lock, puis démarrage de la
    /// boucle de riposte (ordre capture 17:22:58.827-58.945).</summary>
    private async Task MeleeEngageDelayedAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int dmg,
        int generation)
    {
        try
        {
            if (!st.Migrated ||
                !mob.IsCombatTarget(generation, (int)st.CharId))
                return;
            // 271 DisplayMonster — corps natif 10B
            // [mob][nbEnchants 0][gen][owner] (CAI::ChangeOwner →
            // BroadCastToCanSee).
            await BroadcastDisplayMonsterAsync(mob.Handle, st.NodeId, mob.InstanceKey,
                st.CharId, generation);
            if (!mob.IsCombatTarget(generation, (int)st.CharId)) return;
            await SendMobAggroAsync(session, mob, CancellationToken.None);
            if (!mob.IsCombatTarget(generation, (int)st.CharId)) return;
            // AUCUN 302 pour le joueur ici — les paires `0020 sel=4`/
            // `0000 sel=4` de la capture correspondent EXACTEMENT aux
            // casts (D NCID=9 : 58.547, 01.880, 08.153, 09.701, 10.856…)
            // — ce sont des pulses de cast, jamais de la mêlée pure.
            await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
                CancellationToken.None);
            if (!mob.IsCombatTarget(generation, (int)st.CharId)) return;
            await SendAttrAsync(session, mob.Handle, 0x13, mob.Patience, 0,
                CancellationToken.None);
            if (!mob.IsCombatTarget(generation, (int)st.CharId)) return;
            await SendMeleeAsync(session, st.CharId, mob.Handle, dmg, 3,
                -1, CancellationToken.None);
            if (!mob.IsCombatTarget(generation, (int)st.CharId)) return;
            await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget
            {
                Field1 = (int)mob.Handle, Field2 = 0,
                Field3 = (int)st.CharId,
            }, CancellationToken.None);
            StartMobCombatLoop(session, st, mob);
        }
        catch { /* session morte */ }
    }

    /// <summary>Mêlée joueur→joueur : handle = charId de la cible sur le
    /// même node. Envoie la séquence native aux deux sessions (272
    /// résultat + -1, 280 attr0/0x11 HP cible, swings 302 des deux côtés).
    /// Retourne true si la cible a été résolue et frappée.</summary>
    /// <summary>Hostilité joueur↔joueur — port du noyau char→char de
    /// <c>CCharacter::IsMyEnemy</c> @0x0840e490 : hors contexte PvP
    /// partagé, tout joueur est « ami » (jamais une cible) — les bits
    /// natifs testés sont les champs posés par les pipelines versus/
    /// territoire/battlefield/croisade/BR et le duel. Le flag
    /// « chaotique » natif (char+0x3c &amp; 0x40) n'est pas modélisé.</summary>
    internal static bool IsPlayerEnemy(EntryState st, EntryState target)
    {
        if (target.CharId == st.CharId) return false;
        if (st.DuelActive && st.DuelOpponentId == target.CharId
            && target.DuelActive && target.DuelOpponentId == st.CharId)
            return true;
        if (st.VersusInstanceId != 0
            && st.VersusInstanceId == target.VersusInstanceId)
            return st.VersusCamp != target.VersusCamp;
        if (st.BattlefieldId != 0
            && st.BattlefieldId == target.BattlefieldId) return true;
        if (st.CrusadeId != 0
            && st.CrusadeId == target.CrusadeId) return true;
        if (st.BattleRoyaleId != 0
            && st.BattleRoyaleId == target.BattleRoyaleId) return true;
        return st.TerritoryId != 0
            && st.TerritoryId == target.TerritoryId
            && st.TerritoryCamp != target.TerritoryCamp;
    }

    private async Task<bool> TryMeleePlayerAsync(NetSession session,
        EntryState st, uint handle, CancellationToken token)
    {
        if (handle == 0 || handle == st.CharId) return false;
        EntryState? targetSt = null; NetSession? targetSession = null;
        foreach (var s in _services.Sessions.All)
        {
            if (_states.TryGetValue(s.SessionId, out var ost) &&
                ost.CharId == handle && ost.NodeId == st.NodeId &&
                ost.InstanceGroup == st.InstanceGroup &&
                ost.Migrated && !ost.Dead)
            { targetSt = ost; targetSession = s; break; }
        }
        if (targetSt == null || targetSession == null) return false;

        // IsMyEnemy natif : hors contexte PvP partagé (duel/versus/
        // territoire/battlefield/croisade/BR) le joueur est un « ami » —
        // l'attaque ne part pas (410 au caller).
        if (!IsPlayerEnemy(st, targetSt)) return false;

        var dx = targetSt.PosX - st.PosX; var dz = targetSt.PosZ - st.PosZ;
        if (dx * dx + dz * dz > 16f) return false; // hors de portée mêlée

        // PvP — même pipeline natif joueur↔joueur : roll hit/crit +
        // CalculatePhysicalDamage (les deux snapshots sont joueurs —
        // offset de niveau clampé ±10 des deux côtés).
        if (!targetSt.WeaponResolved) await ResolveWeaponAsync(targetSt);
        var pres = NativeCombatFormulas.ResolvePhysicalAttack(
            PlayerSnapshot(st), PlayerSnapshot(targetSt),
            CombatParams(), Random.Shared);
        var dmg = pres.Damage;
        var pvpType = pres.Flags;
        // Bataille de territoire : kills létaux entre camps adverses
        // (TerritoryKilled natif → compteurs terr+0x30/+0x34) ; hors
        // bataille le PvP reste non-létal (clamp à 1 HP existant).
        var lethalTerritory = st.TerritoryId != 0
            && st.TerritoryId == targetSt.TerritoryId
            && st.TerritoryCamp != targetSt.TerritoryCamp;
        // Même logique en match battlefield : la map bf est une
        // arène PvP — kills létaux comptés pour le score (mission).
        var lethalBf = st.BattlefieldId != 0
            && st.BattlefieldId == targetSt.BattlefieldId;
        // Même logique en croisade : les maps de siège sont des
        // arènes PvP entre guildes inscrites (char+0xdfc natif).
        var lethalCrusade = st.CrusadeId != 0
            && st.CrusadeId == targetSt.CrusadeId;
        lethalTerritory = lethalTerritory || lethalBf
            || lethalCrusade;
        targetSt.PlayerHp = lethalTerritory
            ? Math.Max(0, targetSt.PlayerHp - dmg)
            : Math.Max(1, targetSt.PlayerHp - dmg);
        var act = st.InCombat ? st.AttackAct
            : (st.AttackAct = st.NextAction());
        if (!st.InCombat)
        {
            st.InCombat = true;
            await SendPlayerSwingBeginAsync(session, st.CharId, act, token);
        }
        await SendPlayerSwingAsync(session, st.CharId, act, token);
        await SendMeleeAsync(session, st.CharId, handle, dmg, 1,
            NextPlayerAttackKf(st), token);
        await SendAttrAsync(session, handle, 0,
            targetSt.PlayerHp, targetSt.PlayerHp, token);
        await SendMeleeAsync(session, st.CharId, handle, dmg, 1, -1, token);
        try
        {
            // La victime voit le coup + sa propre barre de HP.
            await SendMeleeAsync(targetSession, st.CharId, handle, dmg, 1,
                NextPlayerAttackKf(st), CancellationToken.None);
            await SendVitalHpAsync(targetSession, handle, targetSt.PlayerHp, targetSt.PlayerMaxHp,
                CancellationToken.None);
            await SendPlayerSwingBeginAsync(targetSession, handle,
                targetSt.NextAction(), CancellationToken.None);
            // Mort PvP en bataille — séquence de mort native + kill
            // compté pour le camp du tueur (broadcast 532).
            if (lethalTerritory && targetSt.PlayerHp <= 0
                && !targetSt.Dead)
            {
                await PlayerDeathAsync(targetSession, targetSt,
                    st.CharId, token);
                if (lethalCrusade)
                    await RelayCrusadeKillAsync(st, targetSt);
                else if (lethalBf)
                    await RelayBattlefieldKillAsync(st, targetSt);
                else
                    await RecordTerritoryKillAsync(st, targetSt, token);
            }
        }
        catch { }
        return true;
    }


    /// <summary>Rayon de leash natif : `max_hunt_range =
    /// DAT_087cc688 (0.5u/cellule) × AllFormulaParas[0x4b0]` =
    /// 0.5 × S_Parameter[300] (=100 « 怪物追擊的距離格數 ») →
    /// <b>50 unités</b> entre la cible et l'ancre d'aggro
    /// (CAI+0x44/0x48 = position du mob à l'engagement).</summary>
    private const float GuardRangeSq = 50f * 50f;












    /// <summary>281 ClientSpellHitToLife `[u16 spell][u32 victime]
    /// [u32 caster][i32 amount][i32 flags]` — corps 18B (désasm
    /// `CNC_CZ_ClientSpellHitToLife::Serialize` @0x81e9xxx + construction
    /// dans `OnHitLife` @0x84f26xx : +4=spell, +8=victim+0x34,
    /// +0xc=caster+0x34, +0x10=amount, +0x14=flags). `amount` porte la
    /// valeur de hit quand les champs hit-effect du SSpellData sont
    /// non nuls (`piVar26[4] = local_1c` — captures `2f`=47, `37`=55) ;
    /// il vaut 0 dans la branche `+0xc0/+0xc2/+0xc4 == 0` (capture
    /// `00000000` pour 0x66c3). `flags` voir
    /// <see cref="SpellHitFlags"/> : 1=hit, 8=miss, 0x10=block,
    /// 0x200=restore (affichage vert), 0x200000=crit — les soins
    /// critiquent nativement.</summary>
    private static Task SendSpellHitAsync(NetSession session, int spellId,
        uint victim, uint caster, int amount, int resultFlags,
        CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientSpellHitToLife
        {
            Field1 = (short)spellId,     // s16 : id sort
            Field2 = (int)victim,        // i32 : victime (+0x34)
            Field3 = (int)caster,        // i32 : lanceur (+0x34)
            Field4 = amount,             // i32 : amount natif (0 émission standard)
            Field5 = resultFlags,        // i32 : flags résultat
        }, token);





    /// <summary>300 ClientEnchantRemove `[u32 handle][u16 attr][u8 act]
    /// [i64 0]` — corps 15B exact (capture `2c01 <mob> 36cb 06 00*8`) ;
    /// le i64 final est OBLIGATOIRE : un corps court désynchronise le
    /// flux RC4 (le client lit le ncid suivant sur les octets en trop —
    /// "freeze" sans déconnexion : plus rien n'apparaît).</summary>
    private static Task SendEnchantRemoveAsync(NetSession session,
        ZoneLiveWorld.LiveMob mob, ushort attr, CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientEnchantRemove
        {
            Field1 = (int)mob.Handle,     // i32 : handle
            Field2 = (short)attr,         // s16 : enchant
            Field3 = mob.NextAction(),    // u8  : actionId
            Field4 = 0, Field5 = 0,       // i32 ×2 : +0x10/+0x14 natifs
        }, token);





    /// <summary>Régénération passive HP+MP — tick 5 s. Modèle natif
    /// (scheduler CCharacter, zone/decompiled.c ~0x80fff50) : hors
    /// combat → RecoverHP+RecoverMP ; flag combat (char+0x3c&amp;4) → MP
    /// SEUL à ¼ de cadence ; assis → taux 1.0 vs 0.25 debout (×4).
    /// Base 5 %/tick par pool (gameplay constaté natif). Émet attr 0x11
    /// self + attr 0 broadcast pour HP, attr 0x12 self pour MP (les
    /// formats utilisés par le burst de changement de classe —
    /// capture zsniff 17:22:35.925).</summary>
    private Task PlayerRegenLoopAsync(NetSession session, EntryState st,
        CancellationToken token)
    {
        // Point d'entrée : arme le premier pas — chaque tick se
        // reschedule sur la boucle (pas de tâche while+Delay : les
        // mutations HP/MP vivent sur le fil unique).
        ScheduleOnLoop(TimeSpan.FromSeconds(5),
            _ => PlayerRegenStepAsync(session, st, token), token);
        return Task.CompletedTask;
    }

    /// <summary>Un tick de régénération — corps de l'ancienne boucle
    /// while, reschedule sur la boucle tant que la session vit.</summary>
    private async Task PlayerRegenStepAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        // Un échec de tick (save DB, session coupée) ne doit JAMAIS
        // tuer la boucle — le catch global d'avant arrêtait la regen
        // sans laisser de trace (bug live : aucune regen HP/MP).
        try
        {
                if (!st.Migrated) return;
                // Autosave ~1 min (12 ticks × 5 s) — un kill du process
                // ne doit pas perdre niveau/XP/position (constaté live).
                if (++st.RegenTicks % 12 == 0)
                    await SaveCharacterAsync(st);
                if (st.Dead || st.PlayerHp <= 0) goto Reschedule;

                // HP : hors combat seulement (natif : combat = MP seul).
                if (!st.InCombat && st.PlayerHp < st.PlayerMaxHp)
                {
                    var hpTick = Math.Max(1, st.PlayerMaxHp / 20); // 5 %
                    if (st.Sitting) hpTick *= 4;
                    st.PlayerHp = Math.Min(st.PlayerMaxHp,
                        st.PlayerHp + hpTick);
                    await SendVitalHpAsync(session, st.CharId, st.PlayerHp, st.PlayerMaxHp,
                        CancellationToken.None);
                    await SendAttrAsync(session, st.CharId, 0,
                        st.PlayerHp, st.PlayerHp, CancellationToken.None);
                }

                // MP : toujours — ÷4 en combat (natif : taux 0.25).
                if (st.PlayerMp < st.PlayerMaxMp)
                {
                    var mpTick = Math.Max(1, st.PlayerMaxMp / 20); // 5 %
                    if (st.Sitting) mpTick *= 4;
                    else if (st.InCombat) mpTick = Math.Max(1, mpTick / 4);
                    st.PlayerMp = Math.Min(st.PlayerMaxMp,
                        st.PlayerMp + mpTick);
                    await SendVitalMpAsync(session, st.CharId, st.PlayerMp, st.PlayerMaxMp,
                        CancellationToken.None);
                    EdenLog.Write($"[Regen] char=#{st.CharId} " +
                        $"mp={st.PlayerMp}/{st.PlayerMaxMp} " +
                        $"hp={st.PlayerHp}/{st.PlayerMaxHp}");
                }
            }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[Regen] tick erreur char={st.CharId} : {ex.Message}");
        }
    Reschedule:
        // Re-schedule tant que la session vit — l'annulation du token
        // (déconnexion) expire silencieusement l'événement.
        if (st.Migrated)
            ScheduleOnLoop(TimeSpan.FromSeconds(5),
                _ => PlayerRegenStepAsync(session, st, token), token);
    }

    private static int _blastSerial;

}
