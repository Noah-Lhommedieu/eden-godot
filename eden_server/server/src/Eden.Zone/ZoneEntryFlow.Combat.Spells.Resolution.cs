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
using Eden.Zone.Combat.SpellEffects;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Partiel sorts — résolution des impacts :
/// hit déclaré, aggro/impact schedulés, application des
/// commandes de résultat, casts position/nowhere et leurs
/// releases. La séquence de cast vit dans Combat.Spells.cs.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Résolution d'un sort sur cible — séquence capture native
    /// (zsniff 17:22:58.547 → 17:23:02.490) :
    ///   immédiat : `279 [u16 spell][u32 0][u32 mob][u8 1]` →
    ///     `302 char 0020 sel=0` (début de chaîne d'actions, nouvel act)
    ///     ou `0020 sel=4` (chaîne déjà ouverte, même act) → `288 Blast` ;
    ///   temps `hit` de la motion (swing) : engage éventuel
    ///     `271 [mob][00 01][char]` +
    ///     `302 mob aggro` + `280 mob attr0/attr13` + `444 [mob][0][char]`,
    ///     et `302 char 0020 sel=4` si flag[0] vient d'être posé ;
    ///   fin de swing+vol : `302 char 0000 sel=4` (action terminée) +
    ///     `328 combat tick` → dégâts appliqués →
    ///     `280 mob attr0/attr13` → `281 [spell][mob][char][dmg][1]` →
    ///     survie : `280 attr0x21` + `299` ; mort : `305` + `300` + exit.
    /// L'act reste constant pendant toute la chaîne (0x1e dans la
    /// capture) ; la sortie prend un nouvel act (0x1f).</summary>
    /// <summary>Shape dérivée `SSpellData+0x18` — switch natif de
    /// `LoadAllSpellData` @0x08380130 sur `CSpellData+0x44`
    /// (SpellDef.Param1 = S_Spell col21) combiné à `+0xcc`
    /// (SpellDef.ShapeSub = col31) : 0→{3,5}, 1-3→3, 4→{0,2,5,6},
    /// 5→{1,2,5,6}, 6→0. Valeurs = clés de générateurs
    /// (enregistrement @0x0815xxxx) : 0=CSpellToLife (ciblé),
    /// 1=CSpellToLocation (NCID16, AoE au sol), 2=direction,
    /// 3=CSpellToNowhere (sans cible, NCID17), 4=CSpellToNearby
    /// (jamais produit par la table), 5=croix, 6=secteur.
    /// `CastToLife` @0x081545a0 : 2/5/6 → AoE à la position de la
    /// cible.</summary>
    private static int DerivedShape(SpellParser.SpellDef def) =>
        def.Param1 switch
        {
            0 => def.ShapeSub == 3 ? 5 : 3,
            >= 1 and <= 3 => 3,
            4 => def.ShapeSub == 2 ? 6 : def.ShapeSub == 3 ? 5
                : def.ShapeSub == 1 ? 2 : 0,
            // cas 5 natif : sub==1→2, sub==2→6, sub==3→5,
            // sinon 1 (ToLocation) ; +0x6e==0 → forcé 1.
            5 => def.ShapeSub == 2 ? 6 : def.ShapeSub == 3 ? 5
                : def.ShapeSub == 1 ? 2 : 1,
            _ => 0,         // case 6 et hors table → mono-cible
        };

    /// <summary>Spec de ciblage du sort — `SSpellData+0x1a` dérivé dans
    /// `CSpellDBO::LoadAllSpellData` @0x08380130 : switch sur
    /// `CSpellData+0x44` (S_Spell index 21 = <see
    /// cref="SpellParser.SpellDef.Param2"/>) combiné au type (index 18 =
    /// <see cref="SpellParser.SpellDef.Type"/>) :
    ///   0/1/2 → Type==1 ? -99 : -98 ; 3 → -96 ; 4/5 → Type==1 ? -99 :
    ///   (Type∈{3,5} ? -96 : -98) ; 6 → -93 ; init/autre → -91.
    /// `+0x1a` est lu par `CEffectFactory::CanCastTarget` @0x0805f700 :
    /// -99=IsMyEnemy(vfunc+0x7c), -98=IsMyFriend(+0x80 — inclut le
    /// lanceur et retourne vrai pour tout non-caractère),
    /// -97/-96=IsMyTeam(+0x84 ; -96 inclut aussi le lanceur),
    /// -95/-94=ami (ennemi quand Type==1 — absorbé dans le switch),
    /// toute autre valeur → cible toujours valide.</summary>
    private static int SpellTargetSpecOf(SpellParser.SpellDef def) =>
        def.Param2 switch
        {
            0 or 1 or 2 => def.Type == 1 ? -99 : -98,
            3 => -96,
            4 or 5 => def.Type == 1 ? -99
                : def.Type is 3 or 5 ? -96 : -98,
            6 => -93,
            _ => -91,
        };

    /// <summary>Vrai si la spec +0x1a désigne les ENNEMIS comme
    /// victimes (-99, ou -93/-91 = toute entité). Un cercle ami
    /// (-98/-97/-96/-95/-94) ne doit jamais entrer le pipeline hostile
    /// (dégâts + provocation) — c'est ce qui faisait aggro tous les
    /// mobs sur un buff de groupe (bug live 50107).</summary>
    private static bool SpellSpecHostile(int spec) =>
        spec is -99 or -93 or -91;

    /// <summary>Rayon AoE natif = `(short)(u16(CSpellData+0x6e) ×
    /// 0.5f)` = S_Spell col30 × 0.5 (SpellDef.Duration), TRONQUÉ
    /// entier (LoadAllSpellData @0x08380130). 0 = pas de zone —
    /// les HitTest natifs n'énumèrent des victimes que si
    /// `mod(AttrSpellRadius) + base ≥ 1`.</summary>
    private static float AreaRadius(SpellParser.SpellDef def) =>
        def.Duration > 0 ? (int)(def.Duration * 0.5f) : 0f;

    /// <summary>blastIdx du fil — le natif n'envoie PAS la col3 brute
    /// ni -1 : `ISpell::OnBlast` (@0x84f0be0+) appelle
    /// `IKfm::GetBlastID(entity, SSpellData, &id, &motionTime)`
    /// (entity+0x2f8, vfunc+0xc → `CCharKfm::GetBlastID` @0x80e03f0) et
    /// passe le kfId RÉSOLU à `NotifyBlastToSelf`/`NotifyBlastToOthers`
    /// (vfunc+0x44/+0x48). Résolution native de `+0xa0` (col3) :
    /// 401 → pool+0x68, 412 → pool+0x74, 3001-3004 → pools
    /// +0x80/+0x8c/+0x98/+0xa4 (index = classe d'arme), 3091/3092/3094
    /// (0xc13-0xc16) passent avec décalages par arme ; power(+0xc0,
    /// col35) &lt; 1 → défaut pool+0x74 ; col3 vide → -1 PUIS fallback
    /// `GetAttackID` @0x080e0a70 (vfunc+8) = kfId d'attaque de l'arme —
    /// c'est ce que montre la capture (Onrush 50038 → wire 0x3fe).
    /// Le -1/expédié brut du lot14 ne résolvait aucun kf côté client →
    /// aucune anim des skills physiques (thief/warrior). À cheval :
    /// remap catégoriel natif de la col3 (LAB_080e0510, char+0x3b bit2
    /// && char+0x824 ≠ 0).</summary>
    private int BlastWireId(EntryState st, SpellParser.SpellDef? def)
    {
        var col3 = def?.BlastId ?? 0;
        if (RidingCombatMount(st))
            return MotionParser.RideBlastRemap(col3);
        var resolved = MotionParser.ResolveShootBlastId(col3,
            MotionParser.ShootIndex(st.WeaponKind, st.WeaponOffKind),
            def?.DamagePower ?? 0);
        // -1/0 non résolu → GetAttackID natif : la motion d'attaque de
        // l'arme équipée (piochée random dans le pool, comme la mêlée).
        return resolved > 0 ? resolved : NextPlayerAttackKf(st);
    }

    /// <summary>Post-traitement `CCharFormula::SpellDamage` @0x83f74c0
    /// — modificateurs par spellId : d'abord le taux (arbre
    /// char+0x720, cmd 2064 `AttrSpellDamageRate` →
    /// `dmg × (100+rate)/100`), puis le flat (arbre +0x704, cmd 2063
    /// `AttrSpellDamage` → `dmg + v`). Kinds 8/7 de
    /// <c>SpellAttrMods</c>.</summary>
    private static int SpellDamageMod(EntryState st, int spellId,
        int dmg)
    {
        if (st.SpellAttrMods.TryGetValue((8, spellId), out var rate)
            && rate != 0)
            dmg = (int)((100L + rate) * dmg / 100);
        if (st.SpellAttrMods.TryGetValue((7, spellId), out var flat)
            && flat != 0)
            dmg += flat;
        return dmg;
    }

    private async Task ExecuteSpellHitAsync(NetSession session, EntryState st,
        ushort spellId, ZoneLiveWorld.LiveMob mob, CancellationToken token,
        bool skipAck = false)
    {
        var def = _services.Spells.GetDefinition(spellId);

        // Coût MP natif `CCharFormula::CostMp` @0x83f44d0 : base
        // (S_Spell col32 → SSpellData+0xbc) × coef palier
        // (S_SpellReference col1 → ref+4) × 0.01, arrondi. Le CHECK se
        // fait au cast ; la DÉDUCTION réelle se fait au blast
        // (`ISpell::OnBlast` @0x084f0be0 : `char+0x70 -= CostMp` puis
        // `CNC_CZ_ClientModifyAttr` 328 sel=1 valeur=-cost — voir
        // SpellHitImpactAsync). MP insuffisante → 410 fail comme une
        // cible morte (le client relâche son attente de cast au lieu
        // de se figer).
        var mpCost = def is null
            ? 0 : SpellMpCost(def, await PlayerSpellLevelAsync(st, spellId),
                flat: (int)PlayerSnapshot(st).BonusMpCostFlat);
        if (mpCost > 0 && st.PlayerMp < mpCost)
        {
            await session.SendPacketAsync(410, Concat(
                BitConverter.GetBytes(9), BitConverter.GetBytes(0)),
                token);
            return;
        }

        // Down-time armé à la résolution réussie (natif :
        // CCharacter::PutInDownTime(SSpellData) @0x842f380 →
        // CSpellMaster::PutInDownTime(entity, spell) = CoolDownTime(
        // entity, SSpellData+0x3c)). Le 368 fusionné (kind=0 items +
        // kind=1 sorts) rafraîchit l'icône grisée côté client.
        if (def is { CooldownDs: > 0 })
        {
            _services.SpellCooldowns.Arm((int)st.CharId, spellId,
                EffectiveCooldownDs(st, spellId, def.CooldownDs));
            await _services.SpellCooldowns.SendUpdateAsync(session,
                (int)st.CharId, _services.ItemCooldowns, token);
        }

        if (!skipAck)
        {
            // 279 CastOK [u16 spell][i32 castTime=0][u32 cible][u8 1]
            // — sorts instantanés (SSpellData+0x38 == 0, capture).
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = 0,
                Field3 = (int)mob.Handle, Field4 = 1,
            }, token);
            // 278 NotifyCastToOthers — `ISpell::OnCast` (@0x084ef270)
            // émet vfunc+0x3c pour TOUT cast réussi, instantané ou non :
            // les témoins voient le départ du geste (flag u8=1 natif).
            // Le lanceur ne reçoit jamais son propre 278.
            await BroadcastToNodePeersAsync(session, st,
                new CNC_CZ_ClientSpellCast
                {
                    Field1 = (short)spellId, Field2 = (int)st.CharId,
                    Field3 = (int)mob.Handle, Field4 = 1,
                    Field5 = st.PosX, Field6 = st.PosZ,
                });
        }

        // Enchant de résultat du sort (S_Spell col39 → S_Enchant id) :
        // c'est LÀ que vit la charge — pas dans les flags col19
        // (SSpellData+0x10 = conditions de cast, CheckOpFlag désasm).
        // Ex. Onrush 50038 → 52038 = `1802 0 2` = DistanceGoto(dist 0)
        // → le caster charge sur la cible (CEC_DistanceGoto::Execute
        // @0x85239xx : dist==0 → SetPos(caster→cible) ; dist>0 →
        // knockback de la CIBLE le long de sa facing).
        if (def is { ResultSpellId: > 0 })
            await ApplySpellResultCommandsAsync(session, st, spellId, def,
                mob, token);

        // Pipeline natif `HitMagicSkill`/`HitPhysicalSkill`/
        // `HitRestoreSkill` (@0x84ef710/@0x84f1a00/@0x84f1f40 — dispatch
        // flags SSpellData+0x94, type col18) : dégâts power col35 × coef
        // palier, OU soin power col36 × coef heal (palier col4) pour les
        // sorts type 4/5 ; rolls hit/crit natifs.
        var spLvl = await PlayerSpellLevelAsync(st, spellId);
        var kind = def != null
            ? NativeCombatFormulas.SpellHitKindOf(def)
            : SpellHitKind.None;
        var healRes = kind == SpellHitKind.Restore
            ? NativeCombatFormulas.ResolveSpellHeal(PlayerSnapshot(st),
                MobSnapshot(mob), def!, SpellRefCoef(def!, spLvl),
                CombatParams(), Random.Shared)
            : default;
        var sres = kind is SpellHitKind.Physical or SpellHitKind.Magic
            ? NativeCombatFormulas.ResolveSpell(PlayerSnapshot(st),
                MobSnapshot(mob), def!, SpellRefCoef(def!, spLvl),
                CombatParams(), Random.Shared,
                k => GetSpellAttrModify(st, k, spellId))
            : new PhysicalResult(0, PhysicalOutcome.Hit);
        var dmg = SpellDamageMod(st, spellId, sres.Damage);
        // Flags natifs du 281 : restore → 0x200 (+0x200000 si crit et
        // soin effectif) ; sinon hit/miss/crit via SpellHitFlagsOf.
        var hitFlags = kind == SpellHitKind.Restore
            ? SpellHitFlags.Restore |
                (healRes.Critical && healRes.Amount > 0
                    ? SpellHitFlags.Crit : 0)
            : NativeCombatFormulas.SpellHitFlagsOf(sres);

        // Provocation native d'OnHitLife : `vfunc+0x94` (OnHit) n'est
        // appelé que dans la branche dégâts, et un enchant appliqué ne
        // provoque que s'il est hostile (OnBeAttackEnchant natif). Un
        // sort ciblé sur un mob SANS dégât et SANS enchant hostile
        // (heal, buff lancé par erreur) n'engage pas le combat —
        // le mob reçoit juste le 281.
        var provoke = dmg > 0 ||
            (def is { ResultSpellId: > 0 } &&
                EnchantIsHostile(def.ResultSpellId));
        var first = provoke && (!mob.InCombat ||
            mob.TargetCharId != (int)st.CharId);
        var chainStart = !st.ActionChain;
        if (chainStart)
        {
            st.AttackAct = st.NextAction();
            st.ActionChain = true;
        }
        if (provoke)
        {
            st.InCombat = true;
            BeginMobCombat(st, mob);
            mob.LastHitAt = DateTime.UtcNow;
        }

        // 302 action : sel = bit18 « bataille » du mot d'état —
        // st.InCombat à l'émission (sel=4 si le cast provoque,
        // sinon 0 — un heal/buff sur mob n'allume pas la stance).
        await SendFlagAsync(session, st.CharId, 0x2000,
            st.InCombat ? SEL_COMBAT : (byte)0, 1, st.AttackAct, token);

        // 288 ClientSpellBlastOK — corps natif exact (désasm
        // CNC_CZ_ClientSpellBlastOK::Serialize) :
        // [u32 serial][i32 cible][s16 spell][i32 power]
        // [f32 x][f32 y][f32 z][i32 blastId][u16 n][n×f32][u16 m][m×f32][u8]
        // blastId = kfId RÉSOLU (GetBlastID natif — voir BlastWireId) :
        // référence de pool traduite par classe d'arme, ou kf d'attaque
        // quand la col3 est vide (skills physiques — capture : 0x3fe).
        var power = def?.Power ?? 20;
        _blastSerial = (_blastSerial + 11) & 0x7fffffff;
        var serial = (uint)_blastSerial;
        var blastId = BlastWireId(st, def);
        await session.SendAsync(new CNC_CZ_ClientSpellBlastOK
        {
            Serial = serial,
            VictimHandle = (int)mob.Handle,
            SpellId = unchecked((short)spellId),
            Power = power,
            X = mob.X, Y = mob.Y, Z = mob.Z,
            BlastId = blastId,
        }, token);

        // 289 ClientSpellBlastToLife — visuel de blast vu par les
        // tiers : CSpellToLife::NotifyBlastToOthers @0x084f7e40
        // diffuse à CanSee(lanceur) puis CanSee(cible) — le lanceur
        // n'est JAMAIS dans son propre CanSee (param_1 sauté, comme
        // le 278) : il ne reçoit le 289 que via le NotifyClient de la
        // CIBLE quand celle-ci est un joueur (target+4 ≠ 0 &&
        // +0x24 == 0). Cible mob → le natif n'émet AUCUN 289 au
        // lanceur (preuve : zéro 289 dans les captures zsniff). Le
        // 289 reçu par son propre acteur rejouait/resetait la motion
        // du cast → anim coupée dès son départ (bug live).
        // Corps (Serialize @0x081e8870) : [u32 serial][s16 spell]
        // [i32 hLanceur (this+0x18)][f32 srcXYZ][i32 hCible (this+0x1c)]
        // [f32 dstXYZ][i32 blastIdx][i32 0].
        var blastToLife = new CNC_CZ_ClientSpellBlastToLife
        {
            Field1 = serial,
            Field2 = unchecked((short)spellId),
            Field3 = (int)st.CharId,      // handle lanceur (this+0x18)
            Field4 = st.PosX, Field5 = st.PosZ, Field6 = 0f,
            Field7 = (int)mob.Handle,     // handle cible (this+0x1c)
            Field8 = mob.X, Field9 = mob.Y, Field10 = mob.Z,
            Field11 = blastId, Field12 = 0,
        };
        await BroadcastToNodePeersAsync(session, st, blastToLife);

        if (first)
            StartMobCombatLoop(session, st, mob);

        // --- Résolution différée — timing NATIF, pas de délai fixe ---
        // swing = temps 'hit' de la motion blast dans le KFM du perso
        // (CKfm::GetMotionTime @0x080e0b00 → EventSpellBlasting
        // @0x0866d6e0) ; impact = swing + vol du projectile
        // (CSpellToLife::GetBlastTime @0x084f7db0 = dist/flySpeed).
        // Schedulé sur la boucle : un Task.Delay suspendrait le fil
        // de jeu (toutes les phases d'impact passent par Schedule).
        var flyMs = FlyDelayMs(st, mob, def);
        ScheduleOnLoop(
            TimeSpan.FromMilliseconds(SwingDelayMs(st, def, blastId)),
            _ =>
            SpellHitAggroAsync(session, st, mob, spellId, first,
                chainStart, kind, healRes, dmg, hitFlags, flyMs, mpCost,
                def));
    }
    /// <summary>Phase swing (temps `hit` de la motion) d'<see
    /// cref="ExecuteSpellHitAsync"/> : affichage de l'aggro
    /// (271/444/attrs) et pulse flag[4]=0x2000. Enchaîne l'impact au
    /// bout du vol projectile (`flyMs`).</summary>
    private async Task SpellHitAggroAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, ushort spellId,
        bool first, bool chainStart, SpellHitKind kind,
        SpellHealResult healRes, int dmg, int hitFlags, int flyMs,
        int mpCost, SpellParser.SpellDef? def = null)
    {
        if (st.Migrated && mob.Alive && first)
        {
            // 271 DisplayMonster — corps natif 10B (cf.
            // BroadcastDisplayMonsterAsync).
            await BroadcastDisplayMonsterAsync(mob.Handle, st.NodeId, mob.InstanceKey,
                st.CharId, mob.CombatGeneration);
            await SendMobAggroAsync(session, mob, CancellationToken.None);
            await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
                CancellationToken.None);
            await SendAttrAsync(session, mob.Handle, 0x13, mob.Patience, 0,
                CancellationToken.None);
            await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget
            {
                Field1 = (int)mob.Handle, Field2 = 0,
                Field3 = (int)st.CharId,
            }, CancellationToken.None);
        }
        if (st.Migrated && chainStart)
            // flag[4]=0x2000 aussi quand flag[0] porte l'action
            // (capture 58.828) — sel reflète le combat réel (un cast
            // non-hostile n'allume pas la stance).
            await SendFlagAsync(session, st.CharId, 0x2000,
                st.InCombat ? SEL_COMBAT : (byte)0, 1,
                st.AttackAct, CancellationToken.None);

        // --- Fin de l'action + coût MP : l'événement natif
        // `CEventSpellBlasting` (@0x0866d6e0 → `ISpell::OnBlast`
        // @0x084f0be0) tourne au temps `hit` de la motion — capture :
        // `302 0000 sel=4` + `328 ModifyAttr` à ~+600 ms, AVANT le 281
        // (impact à swing+vol, ~+900 ms).
        // Le release DOIT partir même si le mob est mort entre-temps —
        // sinon le state controller client reste en "cast" et bloque
        // tout skill/attaque suivant (bug live "1 skill puis rien").
        if (st.Migrated)
            await SendFlagAsync(session, st.CharId, 0x0000,
                st.InCombat ? SEL_COMBAT : (byte)0, 1,
                st.AttackAct, CancellationToken.None);
        // La chaîne d'action reste OUVERTE pendant tout l'engagement —
        // capture native : l'act reste constant sur toute la chaîne et
        // les casts suivants émettent `0020 sel=4` (pulse flag[4]),
        // jamais une ré-ouverture `0020 sel=0`. La fermeture `0000 sel=0`
        // n'arrive qu'à la sortie de combat (SendCombatExitAsync —
        // mort du mob, stop, deaggro — qui reset aussi ActionChain).
        if (mpCost > 0)
            await ApplyMpCostAsync(session, st, mpCost);

        // --- Dégâts à la fin du vol projectile (EventFly natif — 0
        // quand le sort n'a pas de projectile).
        ScheduleOnLoop(TimeSpan.FromMilliseconds(flyMs), _ =>
            SpellHitImpactAsync(session, st, mob, spellId, kind,
                healRes, dmg, hitFlags, def));
    }
    /// <summary>Phase impact (swing + vol) d'<see
    /// cref="ExecuteSpellHitAsync"/> : application dégâts/soin,
    /// 280 attrs + 281 hit + réaction hurt, mort éventuelle. Le
    /// release flag[4] et le coût MP ont déjà été émis à la phase
    /// swing (<see cref="SpellHitAggroAsync"/>).</summary>
    private async Task SpellHitImpactAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, ushort spellId,
        SpellHitKind kind, SpellHealResult healRes, int dmg,
        int hitFlags, SpellParser.SpellDef? def = null)
    {
        if (!st.Migrated) { st.ActionChain = false; return; }
        if (!mob.Alive)
        {
            // Mob tué entre le 279 et l'impact (auto-attaque, autre
            // joueur) : sans 281 le client gardait son cast « en cours »
            // et refusait tout sort suivant. 410 libère l'action.
            await session.SendPacketAsync(410, Concat(
                BitConverter.GetBytes(9), BitConverter.GetBytes((int)spellId)),
                CancellationToken.None);
            return;
        }

        // Application native : flag 0x200 → la cible REGAGNE des HP
        // (apply vfunc+0x94) ; sinon dégâts. Le post-traitement
        // SpellDamage (mods per-spell 2063/2064) s'applique aussi
        // aux soins (HitRestoreSkill natif l'appelle pareil).
        var healAmount = kind == SpellHitKind.Restore
            ? SpellDamageMod(st, spellId, healRes.Amount) : 0;
        mob.Hp = kind == SpellHitKind.Restore
            ? Math.Min(mob.MaxHp, mob.Hp + healAmount)
            : Math.Max(0, mob.Hp - dmg);
        var hitAmount = kind == SpellHitKind.Restore
            ? healAmount : dmg;
        if (kind != SpellHitKind.Restore)
        {
            await RegisterBpDamageAsync(session, st, mob, def, dmg);
            if (dmg > 0) await TryInterruptMobCastAsync(mob);
        }

        // Splash des sorts à zone ciblée — `CSpellToLife::HitTest`
        // @0x084f8150 : shape 0 (ancrage cible) AVEC rayon ≥ 1 est
        // aussi une AoE circulaire autour de la cible (ex. Tornado
        // 50027 : Param1=4 → shape 0, col30=15 → 7u) ; shapes 2/5/6
        // = direction/croix/secteur à la position de la cible
        // (CastToLife @0x081545a0). Le rayon effectif ajoute le mod
        // kind 1 (AttrSpellRadius). Les victimes sont collectées au
        // HitTest indépendamment du roll de la primaire — le splash
        // part même si celle-ci a miss. Approximation documentée :
        // hit-test circulaire pour les trois shapes directionnelles.
        if (def != null &&
            kind is SpellHitKind.Physical or SpellHitKind.Magic &&
            DerivedShape(def) is 0 or 2 or 5 or 6)
        {
            var splashR = (int)(def.Duration * 0.5f)
                + GetSpellAttrModify(st, 1, spellId);
            if (splashR >= 1)
                await SpellSplashAsync(session, st, mob, spellId, def,
                    kind, mob.X, mob.Y, splashR);
        }
        await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
            CancellationToken.None);
        await SendAttrAsync(session, mob.Handle, 0x13, mob.Patience, 0,
            CancellationToken.None);
        // 281 : le slot amount (+0x10) porte la valeur de hit
        // (`OnHitLife` @0x084f2080 : `piVar26[4] = local_1c` dans la
        // branche à effet — capture `2f000000`=47 pour 0x65c3) ; il
        // vaut 0 quand les champs hit-effect du SSpellData sont nuls
        // (`+0xc0/+0xc2/+0xc4` tous à 0 — capture `00000000` pour
        // 0x66c3). On envoie le dmg calculé : le cas 0 natif correspond
        // à des sorts sans effet de hit direct.
        await SendSpellHitAsync(session, spellId, mob.Handle, st.CharId,
            hitAmount, hitFlags, CancellationToken.None);
        // Réaction "hurt" du mob (SetEventAction natif kf 0x3f7).
        await BroadcastMonsterActionAsync(mob.Handle, st.NodeId, mob.InstanceKey, 0x3f7);

        // Procs « attaque » par victime de sort — OnAttackEnchant
        // natif tourne aussi sur les hits magiques/physiques de
        // sort (miss flag 8 → rien, c'est OnMissEnchant).
        if (kind is SpellHitKind.Physical or SpellHitKind.Magic &&
            (hitFlags & 8) == 0)
            await RunItemProcsAsync(session, st, mob,
                ProcHitEvent.Attack, dmg, CancellationToken.None,
                hitFlags);

        if (mob.Hp <= 0)
        {
            mob.MarkDead();
            mob.DeadAt = DateTime.UtcNow;
            await FinishMobDeathAsync(session, st, mob, true,
                CancellationToken.None);
            return;
        }
        await SendAttrAsync(session, mob.Handle, 0x21, 0x2bc, 0,
            CancellationToken.None);
        // PAS de 299 enchant codé en dur — l'ancienne émission
        // appliquait 0xCB36 = T_Enchant 52022 « Perforation » (DoT +
        // drain MP) à CHAQUE sort réussi, d'où le DoT/flash de teinte
        // constaté live. Le 299 n'est émis que si le sort porte un
        // enchant de résultat réel (branche à câbler quand la colonne
        // S_Spell d'enchant d'impact sera prouvée — capture
        // 17:22:59.448 = enchant du sort joué, pas une constante).
    }

    /// <summary>Splash d'un sort à shape de zone résolu sur une cible
    /// (<see cref="SpellHitImpactAsync"/>) : hit-test circulaire
    /// natif de rayon col30 × 0.5 autour de l'épicentre, puis même
    /// pipeline que <see cref="ApplyAreaSpellAsync"/> — ResolveSpell
    /// par victime (hit/crit natifs), attr 0, 281, aggro de groupe,
    /// mort éventuelle. La cible primaire est exclue (déjà touchée).
    /// </summary>
    private async Task SpellSplashAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob primary, ushort spellId,
        SpellParser.SpellDef def, SpellHitKind kind, float cx, float cz,
        float radius)
    {
        var r2 = radius * radius;
        var spLvl = await PlayerSpellLevelAsync(st, spellId);
        foreach (var mob in _services.Live.MobsOf(st.NodeId,
            st.InstanceGroup).ToList())
        {
            // Pet elf (IsA==0x50b) — IsMyEnemy false natif, immunisé ;
            // cible primaire déjà résolue par le chemin mono-cible.
            if (!mob.Alive || mob.IsElfPet || mob == primary) continue;
            var dx = mob.X - cx; var dz = mob.Y - cz;
            if (dx * dx + dz * dz > r2) continue;

            var sres = NativeCombatFormulas.ResolveSpell(
                PlayerSnapshot(st), MobSnapshot(mob), def,
                SpellRefCoef(def, spLvl), CombatParams(), Random.Shared,
                k => GetSpellAttrModify(st, k, spellId));
            var dmg = SpellDamageMod(st, spellId, sres.Damage);
            if (dmg <= 0) continue;
            var hitFlags = NativeCombatFormulas.SpellHitFlagsOf(sres);
            mob.Hp = Math.Max(0, mob.Hp - dmg);
            mob.SkillHitCharIds.Add((int)st.CharId);
            mob.LastHitAt = DateTime.UtcNow;
            var first = !mob.InCombat ||
                mob.TargetCharId != (int)st.CharId;
            BeginMobCombat(st, mob);
            // Break Point : le drain vaut aussi pour chaque victime d'AoE
            // (HitMagicSkill/HitPhysicalSkill sont appelés par victime).
            await RegisterBpDamageAsync(session, st, mob, def, dmg);
            await TryInterruptMobCastAsync(mob);
            await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
                CancellationToken.None);
            await SendSpellHitAsync(session, spellId, mob.Handle,
                st.CharId, dmg, hitFlags, CancellationToken.None);
            // Procs « attaque » par victime du splash (même règle
            // que la cible primaire — miss exclu).
            if (sres.Outcome != PhysicalOutcome.Miss)
                await RunItemProcsAsync(session, st, mob,
                    ProcHitEvent.Attack, dmg, CancellationToken.None,
                    hitFlags);
            if (mob.Hp <= 0)
            {
                mob.MarkDead();
                mob.DeadAt = DateTime.UtcNow;
                await FinishMobDeathAsync(session, st, mob, true,
                    CancellationToken.None);
            }
            else
            {
                // Enchant résultat appliqué PAR VICTIME du HitTest
                // (TC_TestSpellAttack @0x083c7750 : EffectToLife par
                // entité collectée — knockback, debuff…) — seulement
                // sur les survivants (pas de knockback de cadavre).
                if (def.ResultSpellId > 0)
                    await ApplySpellResultCommandsAsync(session, st,
                        spellId, def, mob, CancellationToken.None);
                if (first)
                    StartMobCombatLoop(session, st, mob);
            }
        }
    }

    /// <summary>Déduction MP native au blast (`ISpell::OnBlast`
    /// @0x084f0be0) : `char+0x70 = cur - CostMp` (clampé au max pour
    /// les coûts négatifs), puis `CNC_CZ_ClientModifyAttr` 328
    /// `[handle][s16 sel=1][i32 -cost][i64 0]` — NotifyClient au
    /// caster ET BroadCastToCanSee aux témoins (deux émissions natif).
    /// Le coût négatif (régénération) irait en `ClientEffectReport`,
    /// non implémenté.</summary>
    private async Task ApplyMpCostAsync(NetSession session,
        EntryState st, int mpCost)
    {
        st.PlayerMp = Math.Max(0, st.PlayerMp - mpCost);
        var mpMod = new CNC_CZ_ClientModifyAttr
        {
            Field1 = (int)st.CharId,       // i32 : handle du lanceur (+0x34)
            Field2 = 1,                    // s16 : attr modifié (1 = MP)
            Field3 = -mpCost,              // i32 : delta (-CostMp)
            Field4 = 0,                    // i64 : aux
        };
        if (st.Migrated)
            await session.SendAsync(mpMod, CancellationToken.None);
        await BroadcastToNodePeersAsync(session, st, mpMod);
    }

    /// <summary>`CZoneServer::BroadCastToCanSee` natif : envoie la
    /// commande à toutes les sessions migrées du même node/instance que
    /// le lanceur, en excluant sa propre session. Même filtre que les
    /// broadcasts existants (blast au sol, spell break).</summary>
    private async Task BroadcastToNodePeersAsync(NetSession session,
        EntryState st, NetCommand cmd)
    {
        var (op, payload) = PreparePacket(cmd);
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup) continue;
            await SendPreparedToAsync(s, op, payload,
                CancellationToken.None);
        }
    }
    /// <summary>Exécute les commandes de l'enchant de résultat d'un
    /// sort (S_Spell col39 → S_Enchant) après un hit réussi — miroir de
    /// `CEffectCommand::Execute` natif. Les commandes sont déléguées à
    /// <see cref="SpellEffectExecutor"/> (handlers prouvés par désasm,
    /// voir Combat/SpellEffects) ; celles sans handler sont loguées
    /// `[Zone] spell-result cmd`, jamais spéculées. Si l'enchant a une
    /// durée (`def.Value` != -1, gate `EffectToLife` @0x8060ec0) il est
    /// aussi posé dans le registre du mob (<see cref="AddMobEnchantAsync"/>)
    /// — debuffs/DoT/verrous y vivent et expirent.</summary>
    private async Task ApplySpellResultCommandsAsync(NetSession session,
        EntryState st, int spellId, SpellParser.SpellDef? def,
        ZoneLiveWorld.LiveMob mob, CancellationToken token)
    {
        if (def is not { ResultSpellId: > 0 }) return;
        var enchantId = def.ResultSpellId;
        var host = new MobSpellEffectHost(this, session, st, token);
        var ctx = new SpellEffectContext((int)st.CharId, st.PosX,
            st.PosZ, mob, enchantId);
        foreach (var c in _services.Enchants.GetUseCommands(enchantId))
        {
            if (await SpellEffects.TryExecuteAsync(host, ctx, c))
                continue;
            var name = EnchantCommands.Names.TryGetValue(c.CommandId,
                out var n) ? n : $"#{c.CommandId}";
            EdenLog.Write($"[Zone] spell-result cmd " +
                $"enchant={enchantId} {name} " +
                $"args=[{string.Join(',', c.Args)}]");
        }
        // Enchant persistant — même gate qu'EffectToLife : dur != -1
        // crée le CEnchant ; -9 = durée propre de l'effet, icône cachée.
        // La chance c40 est gérée par les call-sites (voie joueur) —
        // ici chaque hit applique, fidèle au HitTest natif par entité.
        // Un enchant n'est posé sur le MOB que si le sort vise des
        // ennemis (spec -99/-93/-91) : un buff/soin ami lancé avec un
        // mob ciblé ne doit pas apparaître comme status de l'ennemi
        // (bug live « sorts sans effet marqués status sur l'ennemi »).
        if (def.Value != -1 && !IsInstantOnlyEnchant(enchantId) &&
            SpellSpecHostile(SpellTargetSpecOf(def)))
        {
            var level = await PlayerSpellLevelAsync(st, spellId);
            await AddMobEnchantAsync(session, st, mob, (int)st.CharId,
                enchantId, def.Value + GetSpellAttrModify(st, 9, spellId),
                level, def.Value == -9, token);
        }
    }

    /// <summary>Pont <see cref="ISpellEffectHost"/> → flux zone (voie
    /// mob) : émet les paquets natifs de déplacement.</summary>
    private sealed class MobSpellEffectHost(
        ZoneEntryFlow flow, NetSession session, EntryState st,
        CancellationToken token)
        : ISpellEffectHost
    {
        public async Task MoveCasterAsync(float x, float z)
        {
            st.PosX = x;
            st.PosZ = z;
            await session.SendAsync(new CNC_CZ_ClientGoto
            {
                Field1 = (int)st.CharId, Field2 = st.PosX,
                Field3 = st.PosZ, Field4 = 0x6a,
            }, token);
        }

        public async Task MoveMobAsync(ZoneLiveWorld.LiveMob mob,
            float x, float y)
        {
            st.EntityPos[mob.Handle] = (x, y);
            await SendMovePredictionAsync(session, mob.Handle,
                x, y, x, y, token);
        }

        /// <summary>Dispel : le handler retire du registre du mob et
        /// émet le 300 aux témoins — délégué au registre du flow.</summary>
        public Task RemoveMobEnchantAsync(ZoneLiveWorld.LiveMob mob,
            int effectId)
            => flow.RemoveMobEnchantAsync(session, st, mob, effectId,
                token);

        /// <summary>7003 — pose le sous-effet sur un mob via le
        /// registre (rest -9 = icône cachée, même convention que la
        /// voie principale).</summary>
        public Task ApplyEnchantToMobAsync(ZoneLiveWorld.LiveMob mob,
            int effectId, int restSec, int level)
            => flow.AddMobEnchantAsync(session, st, mob,
                (int)st.CharId, effectId, restSec, level,
                restSec == -9, token);

        /// <summary>7003 mode 1/3 — pose le sous-effet sur le
        /// lanceur joueur (voie status standard).</summary>
        public Task ApplyEnchantToCasterAsync(int effectId,
            int restSec, int level)
            => flow.AddPlayerEnchantAsync(session, st,
                (int)st.CharId, effectId, restSec, level, token);

        /// <summary>7003 sélection joueurs — côté ami : migrés du
        /// node dans le rayon moins les ennemis du lanceur ; côté
        /// ennemi (<paramref name="foes"/>) : les hostiles seuls.</summary>
        public Task<int> ApplyEnchantToPlayersNearAsync(float x,
            float z, float radius, int effectId, int restSec,
            int level, bool foes = false)
            => flow.ApplyEnchantToPlayersNearAsync(session, st, x, z,
                radius, effectId, restSec, level, foes, token);

        /// <summary>7003 sélection ennemi — mobs vivants du node dans
        /// le rayon.</summary>
        public IEnumerable<ZoneLiveWorld.LiveMob> MobsNear(float x,
            float z, float radius)
            => flow.MobsNear(st, x, z, radius);

        /// <summary>2047 — soin flat des vitals du lanceur.</summary>
        public Task HealCasterAsync(int hpDelta, int mpDelta)
            => flow.HealCasterFromEffectAsync(session, st, hpDelta,
                mpDelta, token);

        /// <summary>1913/1807 — CNC_CZ_ClientPlayEffect au lanceur +
        /// can-see (broadcast node) ; handle = control id natif.</summary>
        public Task PlayEffectAsync(float a0, float a1, float a2,
            int a3, float a4, uint handle = 0)
            => flow.PlayCasterEffectAsync(session, st, a0, a1, a2,
                a3, a4, handle, token);

        /// <summary>1999 — le mob recaste le sort vers l'attaquant
        /// (pipeline de cast mob existant, wind-up honoré).</summary>
        public Task MobCounterCastAsync(ZoneLiveWorld.LiveMob mob,
            int spellId)
            => flow.MobCounterCastAsync(session, st, mob, spellId,
                token);
    }
    /// <summary>SpellCastToLocation `[u16 spell][i32][f32 x][f32 z]` —
    /// sorts de zone ciblés au sol (tornado, pluie de feu). Port de
    /// `CSpellFactory::CastToLocation` @0x81543a0 : le natif dispatche
    /// sur `SSpellData+0x18` (1=cercle, 2=direction, 5=croix,
    /// 6=secteur) puis le générateur fait un HitTest circulaire de
    /// rayon `spell+0x48` (+SpellAttrModify/2) autour du point
    /// (`CSpellToLocation::HitTest` @0x084f9300). Notre port : le
    /// rayon = S_Spell col29 × 0.5 (SSpellData+0x48 — voir
    /// ApplyAreaSpellAsync), repli 8 u quand la donnée est vide.
    /// Le reste du pipeline est celui des sorts ciblés : MP, down-time,
    /// cast temporisé, dégâts `ResolveSpell`, 281 par victime, aggro.</summary>
    private async Task HandleCastLocationAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 14 || st.CharId == 0 || !st.Migrated
            || st.PlayerHp <= 0) return;
        var spellId = BitConverter.ToUInt16(payload, 0);
        var x = BitConverter.ToSingle(payload, 6);
        var z = BitConverter.ToSingle(payload, 10);

        var def = _services.Spells.GetDefinition(spellId);
        if (def is null)
        {
            // Sort hors S_Spell → refus natif ClientCommandFailReturn.
            await session.SendPacketAsync(410, Concat(
                BitConverter.GetBytes(16),
                BitConverter.GetBytes((int)spellId)), token);
            return;
        }
        // StateNoUseSkill (stun/silence) — refus 410 comme CastToLife.
        if ((GetStateLocks(st) &
                (StatusLocks.NoSkillMagic | StatusLocks.NoSkillKungfu))
                != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 16, Field2 = (int)spellId }, token);
            return;
        }
        // Down-time (même règle que CastToLife).
        if (_services.SpellCooldowns.IsCooling((int)st.CharId, spellId))
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 16, Field2 = (int)spellId }, token);
            return;
        }
        // Cast déjà en cours → refus 279 flag=0 (GCD natif).
        if (st.CastingSpell != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = 0,
                Field3 = 0, Field4 = 0,
            }, token);
            return;
        }

        // AoE hostile depuis une monture non-combat → dismount
        // (RemoveEnchantByState(9) natif, même règle que la mêlée).
        await TryAutoDismountForCombatAsync(session, st, token);

        var castMs = def.CastTimeMs;
        if (castMs > 0)
        {
            // Cast temporisé vers un point : mêmes 279/278 que le cast
            // ciblé, la résolution visite le point à l'échéance.
            st.CastCts?.Cancel();
            st.CastCts = new CancellationTokenSource();
            st.CastingSpell = spellId;
            st.CastingTarget = 0;
            var seq = ++st.CastSeq;
            await CastLocationSequenceAsync(session, st, spellId, x, z,
                castMs, seq, st.CastCts.Token, token);
            return;
        }
        await ApplyAreaSpellAsync(session, st, spellId, def, x, z,
            token, skipAck: false);
    }

    /// <summary>Cast temporisé vers un point — miroir de
    /// <see cref="CastSequenceAsync"/> : 279 (castTime ds) + 278
    /// broadcast, puis résolution schedulée sur la boucle.</summary>
    private async Task CastLocationSequenceAsync(NetSession session,
        EntryState st, ushort spellId, float x, float z, int castMs,
        int seq, CancellationToken cancel, CancellationToken token)
    {
        try
        {
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = castMs / 100,
                Field3 = 0, Field4 = 1,
            }, token);
            // 278 broadcast aux témoins — target=0 (non ciblé),
            // position = le point visé (NotifyCastToOthers natif →
            // BroadCastToCanSee(false) : le lanceur exclu).
            await BroadcastToNodePeersAsync(session, st,
                new CNC_CZ_ClientSpellCast
                {
                    Field1 = (short)spellId, Field2 = (int)st.CharId,
                    Field3 = 0, Field4 = 0, Field5 = x, Field6 = z,
                });
            // Flag « casting » (LifeEntityDo 0x0d natif → bit 0x2000) —
            // sel = bit18 « bataille » = st.InCombat à l'émission ;
            // chainStart ne décide que l'act (cf. CastSequenceAsync).
            var chainStart = !st.ActionChain;
            if (chainStart)
            {
                st.AttackAct = st.NextAction();
                st.ActionChain = true;
            }
            await SendFlagAsync(session, st.CharId, 0x2000,
                st.InCombat ? SEL_COMBAT : (byte)0, 1, st.AttackAct,
                token);
        }
        catch
        {
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            return;
        }
        ScheduleOnLoop(TimeSpan.FromMilliseconds(castMs),
            t => ResolveLocationCastAsync(session, st, spellId, x, z,
                seq, t), cancel);
    }

    /// <summary>Résolution à l'échéance d'un cast de zone — même
    /// discipline de séquence/annulation que
    /// <see cref="ResolveCastAsync"/>.</summary>
    private async Task ResolveLocationCastAsync(NetSession session,
        EntryState st, ushort spellId, float x, float z, int seq,
        CancellationToken token)
    {
        // Release du flag « casting » dans TOUS les cas — c'est la
        // sortie d'état qui libère le personnage côté client.
        // `0000 sel=4` avec l'act courant en combat (pulse terminé,
        // chaîne ouverte) ; `0000 sel=0` hors combat ferme la chaîne —
        // jamais sel=0 + act neuf en plein fight (signature de sortie
        // de combat → oscillation/freeze constatés live).
        async Task ReleaseCastFlagAsync()
        {
            if (st.InCombat)
                await SendFlagAsync(session, st.CharId, 0x0000,
                    SEL_COMBAT, 1, st.AttackAct, CancellationToken.None);
            else
            {
                await SendFlagAsync(session, st.CharId, 0x0000, 0, 1,
                    st.AttackAct, CancellationToken.None);
                st.ActionChain = false;
            }
        }

        if (token.IsCancellationRequested)
        {
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            await ReleaseCastFlagAsync();
            return;
        }
        try
        {
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            if (st.CastSeq != seq) { await ReleaseCastFlagAsync(); return; }
            // Mort/démigré pendant le wind-up : le 287 a déjà été émis
            // par BreakCastAsync (via PlayerDeathAsync) — jamais de
            // blast post-mortem.
            if (st.PlayerHp <= 0 || st.Dead || !st.Migrated)
            { await ReleaseCastFlagAsync(); return; }
            var def = _services.Spells.GetDefinition(spellId);
            if (def != null)
                await ApplyAreaSpellAsync(session, st, spellId, def,
                    x, z, CancellationToken.None, skipAck: true);
            else
                await session.SendPacketAsync(410, Concat(
                    BitConverter.GetBytes(16),
                    BitConverter.GetBytes((int)spellId)),
                    CancellationToken.None);
        }
        catch (OperationCanceledException) { await ReleaseCastFlagAsync(); }
        catch
        {
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            try { await ReleaseCastFlagAsync(); } catch { }
        }
    }

    /// <summary>Résolution d'un sort de zone au point (x,z) — le
    /// vrai pipeline natif : coût MP, down-time, 279 + 297, puis pour
    /// chaque victime `ResolveSpell` (hit/crit/dmg natifs) + 281 +
    /// attrs + aggro de groupe (BeginMobCombat + boucle).</summary>
    private async Task ApplyAreaSpellAsync(NetSession session,
        EntryState st, ushort spellId, SpellParser.SpellDef def,
        float x, float z, CancellationToken token, bool skipAck)
    {
        var mpCost = SpellMpCost(def,
            await PlayerSpellLevelAsync(st, spellId));
        if (mpCost > 0)
        {
            if (st.PlayerMp < mpCost)
            {
                await session.SendPacketAsync(410, Concat(
                    BitConverter.GetBytes(16),
                    BitConverter.GetBytes(0)), token);
                return;
            }
            // Déduction + 328 ModifyAttr (caster + CanSee) — voir
            // ApplyMpCostAsync (ISpell::OnBlast natif).
            await ApplyMpCostAsync(session, st, mpCost);
        }
        if (def.CooldownDs > 0)
        {
            _services.SpellCooldowns.Arm((int)st.CharId, spellId,
                EffectiveCooldownDs(st, spellId, def.CooldownDs));
            await _services.SpellCooldowns.SendUpdateAsync(session,
                (int)st.CharId, _services.ItemCooldowns, token);
        }
        if (!skipAck)
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = 0,
                Field3 = 0,                     // i32 : 0 = cast non ciblé
                Field4 = 1,
            }, token);
        // 297 SpellBlastToLocation — Serialize natif @0x081fe6e0 :
        // [u32 serial][s16 spell][i32 power][f32 src xyz]
        // [f32 dst xyz][i32 blastId][u16 n][n×f32][u16 m][m×f32].
        _blastSerial = (_blastSerial + 11) & 0x7fffffff;
        var blast = new CNC_CZ_ClientSpellBlastToLocation
        {
            Serial = (uint)_blastSerial,
            SpellId = unchecked((short)spellId),
            Power = def.Power,
            SrcX = st.PosX, SrcY = st.PosZ, SrcZ = 0f,
            DstX = x, DstY = z, DstZ = 0f,
            BlastId = BlastWireId(st, def),
        };
        await session.SendAsync(blast, token);
        // Broadcast CanSee — les témoins voient l'explosion au sol.
        var (blastOp, blastPayload) = PreparePacket(blast);
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup) continue;
            await SendPreparedToAsync(s, blastOp, blastPayload,
                CancellationToken.None);
        }

        // HitTest circulaire natif (CSpellToLocation::HitTest
        // @0x084f9300) : rayon = *(short*)(SSpellData+0x48) +
        // SpellAttrModify/2. LoadAllSpellData @0x08380130 :
        // +0x48 = (short)(u16(CSpellData+0x6e) × 0.5f) — soit
        // S_Spell col30 × 0.5 (SpellDef.Duration).
        var radius = AreaRadius(def)
            + GetSpellAttrModify(st, 1, spellId) * 0.5f;
        var r2 = radius * radius;
        var spLvl = await PlayerSpellLevelAsync(st, spellId);
        var spec = SpellTargetSpecOf(def);
        if (!SpellSpecHostile(spec))
        {
            // AoE au sol AMI (cercle de soin, buff zone) — les victimes
            // natives sont les alliés du rayon, pas les mobs
            // (CanCastTarget +0x1a). Même pipeline que le cercle ami
            // du cast sans cible, centré sur (x,z).
            await ApplyFriendlyCircleAsync(session, st, def, spellId,
                spec, x, z, (int)radius);
            return;
        }
        var hitAny = false;
        foreach (var mob in _services.Live.MobsOf(st.NodeId,
            st.InstanceGroup).ToList())
        {
            // Pet elf (IsA==0x50b) — IsMyEnemy false natif, immunisé AoE.
            if (!mob.Alive || mob.IsElfPet) continue;
            var dx = mob.X - x; var dz = mob.Y - z;
            if (dx * dx + dz * dz > r2) continue;

            var sres = NativeCombatFormulas.ResolveSpell(
                PlayerSnapshot(st), MobSnapshot(mob), def,
                SpellRefCoef(def, spLvl), CombatParams(), Random.Shared,
                k => GetSpellAttrModify(st, k, spellId));
            var dmg = SpellDamageMod(st, spellId, sres.Damage);
            var hitFlags = NativeCombatFormulas.SpellHitFlagsOf(sres);
            mob.SkillHitCharIds.Add((int)st.CharId);
            // Provocation native : uniquement quand l'impact produit un
            // effet hostile réel (dégâts ou enchant be_attacked) — une
            // AoE sans puissance ne fait pas entrer en combat.
            var hostileHit = def.DamagePower > 0 || dmg > 0 ||
                (def.ResultSpellId > 0 &&
                    EnchantIsHostile(def.ResultSpellId));
            if (hostileHit)
            {
                mob.Hp = Math.Max(0, mob.Hp - dmg);
                mob.LastHitAt = DateTime.UtcNow;
                hitAny = true;
                await RegisterBpDamageAsync(session, st, mob, def, dmg);
            }
            var first = !mob.InCombat ||
                mob.TargetCharId != (int)st.CharId;
            if (hostileHit)
                BeginMobCombat(st, mob);
            await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
                token);
            await SendSpellHitAsync(session, spellId, mob.Handle,
                st.CharId, dmg, hitFlags, token);
            // Procs « attaque » par victime (miss exclu — OnMissEnchant).
            if (hostileHit && sres.Outcome != PhysicalOutcome.Miss)
                await RunItemProcsAsync(session, st, mob,
                    ProcHitEvent.Attack, dmg, token, hitFlags);
            if (mob.Hp <= 0)
            {
                mob.MarkDead();
                mob.DeadAt = DateTime.UtcNow;
                await FinishMobDeathAsync(session, st, mob, true,
                    CancellationToken.None);
            }
            else
            {
                // Enchant résultat par victime du HitTest (natif
                // EffectToLife par entité) — survivants seulement.
                if (def.ResultSpellId > 0)
                    await ApplySpellResultCommandsAsync(session, st,
                        spellId, def, mob, CancellationToken.None);
                if (hostileHit && first)
                    StartMobCombatLoop(session, st, mob);
            }
        }
        // Un sort de zone qui touche met le lanceur en combat —
        // stance native (302 0000 sel4), comme la mêlée.
        if (hitAny && !st.InCombat)
        {
            st.InCombat = true;
            await SendFlagAsync(session, st.CharId, 0x0000,
                SEL_COMBAT, 1, st.NextAction(), token);
        }
    }
    /// <summary>SpellCastToNowhere / ToNearby — buffs soi-même et sorts
    /// sans cible. Chaîne d'actions capture (cast ciblé) :
    /// `279 CastOK` → `302 char 0020 sel=0` (chaîne neuve) → `288 Blast`,
    /// puis release `302 0000` à ~600 ms.
    /// <b>Le sel=4 (SEL_COMBAT) n'est émis que si des dégâts touchent
    /// réellement un ennemi</b> (ApplyNowhereResultAsync) — un cast sans
    /// cible n'entre JAMAIS en combat de lui-même (règle live : le saut
    /// 59558, Type=8/TargetType=1/ResultSpellId=0 dans S_Spell, mettait
    /// le client en stance combat sur un simple saut).</summary>
    private async Task HandleCastNowhereAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        // Wire natif (désasm Deserialize) : [s16 spell][f32 direction].
        if (payload.Length < 6 || st.CharId == 0) return;
        var spellId = BitConverter.ToUInt16(payload, 0);
        var dir = BitConverter.ToSingle(payload, 2);   // f32 : orientation

        // Sort hors S_Spell → le natif `CSpellFactory::CastToNowhere`
        // échoue et renvoie 410 ClientCommandFailReturn
        // [i32 ncid][i32 spell] (désasm 0x87fd1ec / NCID @0x89bc932).
        var def0 = _services.Spells.GetDefinition(spellId);
        if (def0 is null)
        {
            await session.SendPacketAsync(410, Concat(
                BitConverter.GetBytes(17), BitConverter.GetBytes((int)spellId)),
                token);
            return;
        }

        // StateNoUseSkill — même refus que les autres formes de cast.
        if ((GetStateLocks(st) &
                (StatusLocks.NoSkillMagic | StatusLocks.NoSkillKungfu))
                != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 17, Field2 = (int)spellId }, token);
            return;
        }

        // `ISpell::CastTest` natif : cast sur monture non-combat →
        // dismount avant le 279 (même règle que CastToLife).
        await TryAutoDismountForCombatAsync(session, st, token);

        // Coût MP au cast — `ISpell::CastTest` @0x084f04d0 :
        // `char+0x70 < CostMp` → refus (0x5b3 → 410
        // ClientCommandFailReturn). Manquait ici : les buffs NCID 17
        // étaient gratuits (bug live « mana fixe pendant un buff »).
        var mpCost = SpellMpCost(def0,
            await PlayerSpellLevelAsync(st, spellId),
            flat: (int)PlayerSnapshot(st).BonusMpCostFlat);
        if (mpCost > 0 && st.PlayerMp < mpCost)
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 17, Field2 = (int)spellId }, token);
            return;
        }
        // Down-time natif (CheckInDownTime → refus 410, puis
        // PutInDownTime à la résolution) — sans ça un buff se
        // spamme sans cooldown.
        if (_services.SpellCooldowns.IsCooling((int)st.CharId, spellId))
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 17, Field2 = (int)spellId }, token);
            return;
        }
        // Cast déjà en cours (toute forme) : refus natif 279 flag=0 —
        // un silence laisserait le client en attente. Avant l'arm du
        // cooldown : le refus ne doit pas consommer le down-time.
        if (st.CastingSpell != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = 0,
                Field3 = (int)st.CharId, Field4 = 0,
            }, token);
            return;
        }
        if (def0.CooldownDs > 0)
        {
            _services.SpellCooldowns.Arm((int)st.CharId, spellId,
                EffectiveCooldownDs(st, spellId, def0.CooldownDs));
            await _services.SpellCooldowns.SendUpdateAsync(session,
                (int)st.CharId, _services.ItemCooldowns, token);
        }

        // Cast temporisé (S_Spell col26 → CastTimeMs) — même chaîne
        // natif `ISpell::OnCast` que le cast ciblé : 279 porte le vrai
        // castDs (barre client), puis blast+effects à l'échéance.
        var castMs = def0.CastTimeMs;
        if (castMs > 0)
        {
            st.CastCts?.Cancel();
            st.CastCts = new CancellationTokenSource();
            st.CastingSpell = spellId;
            st.CastingTarget = st.CharId;
            var seq = ++st.CastSeq;
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = castMs / 100,
                Field3 = (int)st.CharId, Field4 = 1,
            }, token);
            // 278 NotifyCastToOthers → les témoins voient la gestuelle
            // (BroadCastToCanSee(false) : lanceur exclu).
            await BroadcastToNodePeersAsync(session, st,
                new CNC_CZ_ClientSpellCast
                {
                    Field1 = (short)spellId, Field2 = (int)st.CharId,
                    Field3 = (int)st.CharId, Field4 = 1,
                    Field5 = st.PosX, Field6 = st.PosZ,
                });
            var chainT = !st.ActionChain;
            if (chainT)
            {
                st.AttackAct = st.NextAction();
                st.ActionChain = true;
            }
            await SendFlagAsync(session, st.CharId, 0x2000,
                st.InCombat ? SEL_COMBAT : (byte)0, 1, st.AttackAct,
                token);
            ScheduleOnLoop(TimeSpan.FromMilliseconds(castMs),
                t => ResolveNowhereCastAsync(session, st, spellId, dir,
                    mpCost, seq, t), st.CastCts.Token);
            return;
        }

        await session.SendAsync(new CNC_CZ_ClientSpellCastOK
        {
            Field1 = (short)spellId, Field2 = 0,
            Field3 = (int)st.CharId, Field4 = 1,
        }, token);

        // Ouverture : sel = bit18 « bataille » = st.InCombat à
        // l'émission (le 302 transporte le mot d'état complet —
        // NodifyFlagChange natif). chainStart ne décide que l'act.
        var chainStart = !st.ActionChain;
        if (chainStart)
        {
            st.AttackAct = st.NextAction();
            st.ActionChain = true;
        }
        await SendFlagAsync(session, st.CharId, 0x2000,
            st.InCombat ? SEL_COMBAT : (byte)0, 1, st.AttackAct, token);

        // blastId résolu comme le chemin ciblé (GetBlastID natif —
        // les buffs/self-casts sans col3 rejouent le swing d'arme).
        var blastId = BlastWireId(st, def0);
        await session.SendAsync(new CNC_CZ_ClientSpellBlastOK
        {
            Serial = 1,
            VictimHandle = (int)st.CharId,              // cible = soi
            SpellId = unchecked((short)spellId),
            Power = def0.Power,
            X = st.PosX, Y = st.PosZ, Z = 0f,
            BlastId = blastId,
        }, token);

        // Effets au temps `hit` de la motion de cast (même règle que
        // les sorts ciblés — EventSpellBlasting natif).
        ScheduleOnLoop(
            TimeSpan.FromMilliseconds(SwingDelayMs(st, def0, blastId)),
            _ => NowhereCastReleaseAsync(session, st, spellId, dir,
                mpCost));
    }
    /// <summary>Échéance d'un cast sans cible TEMPORISÉ — miroir de
    /// <see cref="ResolveCastAsync"/> : le flag « casting » (0x2000) et
    /// sa release restent à la convention nowhere
    /// (<see cref="NowhereCastFinishAsync"/> — le break/mort purgent
    /// via <c>BreakCastAsync</c>). À l'échéance : 288 Blast, puis les
    /// effets reprennent le chemin instantané existant
    /// (<see cref="NowhereCastReleaseAsync"/> à +SwingDelayMs).</summary>
    private async Task ResolveNowhereCastAsync(NetSession session,
        EntryState st, ushort spellId, float dir, int mpCost, int seq,
        CancellationToken token)
    {
        // Annulé (break/mort/migration → CastCts), remplacé par un cast
        // plus récent, ou caster mort/démigré pendant le wind-up : ne
        // pas résoudre — jamais de blast post-mortem. Le flag a déjà
        // été libéré par BreakCastAsync ; un cast plus récent possède
        // l'état (ne pas l'écraser).
        if (token.IsCancellationRequested || st.CastSeq != seq ||
            st.PlayerHp <= 0 || st.Dead || !st.Migrated)
        {
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            return;
        }
        st.CastingSpell = 0;
        st.CastingTarget = 0;
        try
        {
            var def = _services.Spells.GetDefinition(spellId);
            if (def == null) return;
            // 288 à l'échéance du cast (ISpell::OnBlast natif après
            // CEventSpellCasting) — même corps que le chemin instantané.
            var blastId = BlastWireId(st, def);
            await session.SendAsync(new CNC_CZ_ClientSpellBlastOK
            {
                Serial = 1,
                VictimHandle = (int)st.CharId,
                SpellId = unchecked((short)spellId),
                Power = def.Power,
                X = st.PosX, Y = st.PosZ, Z = 0f,
                BlastId = blastId,
            }, CancellationToken.None);
            ScheduleOnLoop(
                TimeSpan.FromMilliseconds(SwingDelayMs(st, def, blastId)),
                _ => NowhereCastReleaseAsync(session, st, spellId, dir,
                    mpCost));
        }
        catch { /* session morte — état cast déjà purgé */ }
    }

    /// <summary>Résolution différée d'un cast sans cible :
    ///   +300 ms → effets du sort (sort résultat AoE éventuel) ;
    ///   +600 ms → `302 0000 sel=0` — fin d'action (canal non-combat ;
    ///     si l'AoE a touché des mobs, ApplyNowhereResultAsync a déjà
    ///     posé l'état combat via les attrs/hits, le release combat
    ///     sel=4 partira de la boucle mob).
    /// <b>Mouvement</b> : les OpFlags (S_Spell col19 i64 →
    /// SSpellData+0x10/+0x14) sont des CONDITIONS de cast —
    /// SSpellData::CheckOpFlag (désasm @0x84efae0) les teste (état
    /// entité via virtual +0x1a0, cible, monture…), JAMAIS comme
    /// instruction de déplacement. Le dash de Soul Leap (59101) est
    /// animé et déplacé côté client — le serveur ne téléporte rien
    /// (l'ancien test `OpFlags & 0x04000000` + dash 12u téléportait à
    /// tort tout sort portant ce bit — ex. le sort cléric 51000 → TP
    /// sur la cible puis freeze, bug live).
    ///   ResultSpellId (col 39) = sort d'impact en zone à l'arrivée
    ///   (59280 'Demolishing Thundera') — dégâts 281/280 aux mobs à
    ///   moins de 5u du point d'arrivée et stun serveur 4 s
    ///   (StunnedUntil — supprime la riposte sans paquet spéculatif).
    /// </summary>
    private async Task NowhereCastReleaseAsync(NetSession session,
        EntryState st, ushort spellId, float dir, int mpCost)
    {
        try
        {
            if (!st.Migrated) return;
            var def = _services.Spells.GetDefinition(spellId);
            // Déduction MP au blast — `ISpell::OnBlast` @0x084f0be0 :
            // `char+0x70 -= CostMp` puis 328 ModifyAttr sel=1 au
            // lanceur + broadcast (même endroit que le chemin ciblé).
            if (mpCost > 0)
                await ApplyMpCostAsync(session, st, mpCost);
            // PBAoE native : shape 3 + rayon ≥ 1 → cercle autour du
            // LANCEUR avec les dégâts du sort lui-même
            // (CSpellToNowhere::HitTest @0x084fab70 : cercle quand
            // attrModify + SSpellData+0x48 ≥ 1 — ex. Lightning Web
            // 50032, Blessed Invocation 50408 : col30=15 → 7u).
            // Le chemin « sort résultat en zone » reste pour les
            // sorts sans dégât propre (dash → impact 59280).
            var kind = def != null
                ? NativeCombatFormulas.SpellHitKindOf(def)
                : SpellHitKind.None;
            // ResultSpellId (S_Spell c39) est un ENCHANT pour les
            // buffs/dispels (52028/52056…) — ne JAMAIS le résoudre
            // comme sort de dégâts : GetDefinition renverrait un autre
            // sort au hasard de l'espace d'ids et l'AoE aggro tous
            // les mobs autour (bug live « buff = aggro zone »). Le
            // chemin « sort d'impact en zone » n'est retenu que si
            // l'id résout réellement vers un sort hostile.
            var rdef = def?.ResultSpellId > 0
                ? _services.Spells.GetDefinition(def.ResultSpellId)
                : null;
            var resultIsDamageSpell = rdef != null &&
                NativeCombatFormulas.SpellHitKindOf(rdef) is
                    SpellHitKind.Physical or SpellHitKind.Magic;
            var spec = def != null ? SpellTargetSpecOf(def) : -91;
            if (def != null && DerivedShape(def) == 3 &&
                SpellSpecHostile(spec))
                await ApplyNowherePbaoeAsync(session, st, def,
                    spellId);
            else if (resultIsDamageSpell)
                await ApplyNowhereResultAsync(session, st, def!, token:
                    CancellationToken.None);
            // Cercle AMI (-98/-96/…) : le HitTest natif collecte les
            // entités « amies » dans le rayon — joueurs alliés (soi +
            // équipe pour -96) et entités non-caractères — et applique
            // l'enchant de résultat par victime, SANS aucune entrée en
            // combat (HitRestoreSkill/EffectToLife, jamais Hit*Skill).
            else if (def != null && DerivedShape(def) == 3)
                await ApplyNowhereFriendlyCircleAsync(session, st, def,
                    spellId, spec);
            // Buff self : l'enchant résultat (SSpellData+0x7c, S_Spell
            // c39) s'applique au lanceur via EffectToLife natif —
            // `dur != -1` (c41 ; -9 = durée propre → enchant posé
            // sans icône 299) + roll chance c40. Sans ce pas un buff
            // casté sans cible (NCID 17) n'appliquait rien.
            // Sort hostile (cercle ennemi) : son enchant vise les
            // victimes, jamais le lanceur (bug live « debuffs que
            // j'applique visibles dans MON hud »).
            if (def is { ResultSpellId: > 0 } && def.Value != -1 &&
                !(DerivedShape(def) == 3 && spec == -99) &&
                _services.Enchants.GetUseCommands(def.ResultSpellId)
                    .Count > 0 &&
                (def.ResultSpellChance <= 0 ||
                    Random.Shared.Next(100) < def.ResultSpellChance))
            {
                var lvl = await PlayerSpellLevelAsync(st, spellId);
                await AddPlayerEnchantAsync(session, st,
                    (int)st.CharId, def.ResultSpellId,
                    def.Value + GetSpellAttrModify(st, 9, spellId),
                    lvl,
                    CancellationToken.None, hidden: def.Value == -9);
            }
            // Release schedulé à +300 ms — la phase courante tourne
            // déjà sur la boucle à +300 ms après le cast (site
            // d'appel → ScheduleOnLoop), un Task.Delay suspendrait
            // le fil de jeu.
            ScheduleOnLoop(TimeSpan.FromMilliseconds(300),
                _ => NowhereCastFinishAsync(session, st));
        }
        catch { /* session morte */ }
    }
    /// <summary>Phase finale du cast nowhere (+600 ms) : fermeture
    /// de chaîne ou pulse fin — schedulée par
    /// <see cref="NowhereCastReleaseAsync"/>.</summary>
    private async Task NowhereCastFinishAsync(NetSession session,
        EntryState st)
    {
        try
        {
            if (!st.Migrated) { st.ActionChain = false; return; }
            // Release : `0000 sel=0` (fermeture de chaîne) uniquement
            // hors combat ; si le joueur a engagé entre-temps →
            // `0000 sel=4` = fin du pulse, la chaîne combat reste
            // ouverte (act constant jusqu'à la sortie de combat).
            // La décision regarde st.InCombat À L'ÉMISSION — pas le
            // chainStart capturé 600 ms plus tôt : sinon le cast d'un
            // buff suivi d'une attaque fermait la chaîne combat
            // ouverte entre-temps (oscillation sortie/rentrée).
            if (st.InCombat)
            {
                await SendFlagAsync(session, st.CharId, 0x0000,
                    SEL_COMBAT, 1, st.AttackAct,
                    CancellationToken.None);
            }
            else
            {
                await SendFlagAsync(session, st.CharId, 0x0000, 0, 1,
                    st.AttackAct, CancellationToken.None);
                st.ActionChain = false;
            }
        }
        catch { /* session morte */ }
    }
    /// <summary>PBAoE hostile d'un cast sans cible (shape 3, spec -99 /
    /// -93 / -91) — cercle natif `attrModify + SSpellData+0x48 ≥ 1`
    /// autour du lanceur (CSpellToNowhere::HitTest @0x084fab70). Chaque
    /// mob dans le rayon : ResolveSpell avec la def du sort (hit/crit/
    /// variance/résist par victime), attrs + 281 + aggro/mort ; puis
    /// l'enchant résultat s'applique par victime via les commandes.
    /// <b>Gate `SSpellData+0xc0 != 0` natif</b> (HitMagicSkill
    /// @0x084ef710) : un sort sans `DamagePower` ne touche pas et ne
    /// provoque pas — la branche sans-puissance d'OnHitLife n'émet que
    /// le 281 brut + hate, jamais `vfunc+0x94` (provocation).</summary>
    private async Task ApplyNowherePbaoeAsync(NetSession session,
        EntryState st, SpellParser.SpellDef def, ushort spellId)
    {
        var radius = (int)(def.Duration * 0.5f)
            + GetSpellAttrModify(st, 1, spellId);
        if (radius < 1) return;   // pas de zone → mono (caster/self)
        var r2 = (float)radius * radius;
        var spLvl = await PlayerSpellLevelAsync(st, spellId);
        var hitAny = false;
        foreach (var mob in _services.Live.MobsOf(st.NodeId,
            st.InstanceGroup).ToList())
        {
            if (!mob.Alive || mob.IsElfPet) continue;
            var dx = mob.X - st.PosX; var dz = mob.Y - st.PosZ;
            if (dx * dx + dz * dz > r2) continue;

            if (def.DamagePower <= 0)
            {
                // Branche sans-puissance d'OnHitLife : 281 nu + hate,
                // pas de combat, pas d'aggro (le mob reçoit le paquet
                // de hit mais n'engage pas — pas de dégât, pas de
                // vfunc+0x94 natif).
                await SendSpellHitAsync(session, spellId, mob.Handle,
                    st.CharId, 0, SpellHitFlags.Restore,
                    CancellationToken.None);
                if (def.ResultSpellId > 0)
                    await ApplySpellResultCommandsAsync(session, st,
                        spellId, def, mob, CancellationToken.None);
                continue;
            }

            var sres = NativeCombatFormulas.ResolveSpell(
                PlayerSnapshot(st), MobSnapshot(mob), def,
                SpellRefCoef(def, spLvl), CombatParams(),
                Random.Shared,
                k => GetSpellAttrModify(st, k, spellId));
            var dmg = SpellDamageMod(st, spellId, sres.Damage);
            var hitFlags = NativeCombatFormulas.SpellHitFlagsOf(sres);
            mob.Hp = Math.Max(0, mob.Hp - dmg);
            mob.SkillHitCharIds.Add((int)st.CharId);
            mob.LastHitAt = DateTime.UtcNow;
            hitAny = true;
            var first = !mob.InCombat ||
                mob.TargetCharId != (int)st.CharId;
            BeginMobCombat(st, mob);
            await RegisterBpDamageAsync(session, st, mob, def, dmg);
            await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
                CancellationToken.None);
            await SendSpellHitAsync(session, spellId, mob.Handle,
                st.CharId, dmg, hitFlags, CancellationToken.None);
            // Procs « attaque » par victime du rayon (miss exclu).
            if (sres.Outcome != PhysicalOutcome.Miss)
                await RunItemProcsAsync(session, st, mob,
                    ProcHitEvent.Attack, dmg, CancellationToken.None,
                    hitFlags);
            if (mob.Hp <= 0)
            {
                mob.MarkDead();
                mob.DeadAt = DateTime.UtcNow;
                await FinishMobDeathAsync(session, st, mob, true,
                    CancellationToken.None);
            }
            else
            {
                // Enchant résultat par victime (commandes : knockback,
                // debuff…) — mêmes effets que la branche ciblée.
                if (def.ResultSpellId > 0)
                    await ApplySpellResultCommandsAsync(session, st,
                        spellId, def, mob, CancellationToken.None);
                if (first)
                    StartMobCombatLoop(session, st, mob);
            }
        }
        if (hitAny && !st.InCombat)
        {
            st.InCombat = true;
            await SendFlagAsync(session, st.CharId, 0x0000,
                SEL_COMBAT, 1, st.NextAction(), CancellationToken.None);
        }
    }

    /// <summary>Cercle AMI d'un cast sans cible (shape 3, spec -98 /
    /// -96 / -95 / -94) — les buffs de groupe. Le HitTest natif
    /// collecte les entités « amies » du rayon : le lanceur, les
    /// joueurs alliés (-98 = IsMyFriend, -96 = IsMyTeam + soi) et les
    /// entités non-caractères (IsMyFriend retourne vrai pour tout
    /// non-personnage — les mobs reçoivent les commandes instantanées,
    /// jamais de statut ni de combat). Soigneur : `HealPower` (col36)
    /// par victime (HitRestoreSkill). Buff : l'enchant de résultat par
    /// victime (EffectToLife). JAMAIS de provocation — c'est la règle
    /// « un buff n'aggro pas » (bug live 50107).</summary>
    private async Task ApplyNowhereFriendlyCircleAsync(NetSession session,
        EntryState st, SpellParser.SpellDef def, ushort spellId,
        int spec) =>
        await ApplyFriendlyCircleAsync(session, st, def, spellId, spec,
            st.PosX, st.PosZ, (int)(def.Duration * 0.5f)
                + GetSpellAttrModify(st, 1, spellId));

    /// <summary>HitTest ami générique (centre `cx,cz`, rayon `radius`,
    /// 0 = soi seul) — le lanceur + les alliés du rayon reçoivent le
    /// soin (<see cref="SpellParser.SpellDef.HealPower"/>) et
    /// l'enchant de résultat (EffectToLife natif par victime) ; les
    // mobs du rayon ne reçoivent que les commandes instantanées, sans
    // combat. -96/-97 = team (+soi) ; -98/-95/-94 = tout ami.</summary>
    private async Task ApplyFriendlyCircleAsync(NetSession session,
        EntryState st, SpellParser.SpellDef def, ushort spellId,
        int spec, float cx, float cz, int radius)
    {
        var r2 = (float)radius * radius;

        var teamOnly = spec is -96 or -97;
        TeamEntity? team = null;
        if (teamOnly)
            team = _services.Teams.GetTeamOf((int)st.CharId);
        var victims = new List<EntryState> { st };
        foreach (var ost in _states.Values)
        {
            if (ost == st || !ost.Migrated || ost.PlayerHp <= 0 ||
                ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                ost.Session == null) continue;
            if (radius > 0)
            {
                var dx = ost.PosX - cx; var dz = ost.PosZ - cz;
                if (dx * dx + dz * dz > r2) continue;
            }
            if (teamOnly && (team == null || !team.Members.Exists(m =>
                    m.CharacterId == (int)ost.CharId)))
                continue;
            victims.Add(ost);
        }

        var lvl = await PlayerSpellLevelAsync(st, spellId);
        foreach (var v in victims)
        {
            var vsession = v == st ? session
                : v.Session ?? session;
            // Soin de zone ami — HitRestoreSkill natif par victime.
            if (def.HealPower > 0 && v.PlayerHp > 0)
            {
                var hres = NativeCombatFormulas.ResolveSpellHeal(
                    PlayerSnapshot(st), PlayerSnapshot(v), def,
                    SpellRefCoef(def, lvl), CombatParams(),
                    Random.Shared);
                var real = Math.Min(hres.Amount,
                    Math.Max(0, v.PlayerMaxHp - v.PlayerHp));
                if (real > 0)
                {
                    v.PlayerHp += real;
                    var hFlags = SpellHitFlags.Restore |
                        (hres.Critical ? SpellHitFlags.Crit : 0);
                    await SendSpellHitAsync(vsession, spellId,
                        v.CharId, st.CharId, real, hFlags,
                        CancellationToken.None);
                    await SendVitalHpAsync(vsession, v.CharId,
                        v.PlayerHp, v.PlayerMaxHp,
                        CancellationToken.None);
                }
            }
            // Buff de zone ami — EffectToLife par victime : enchant
            // de résultat avec la même gate que le buff solo (durée
            // != -1, chance) ; -9 = stats actives sans icône.
            if (def is { ResultSpellId: > 0 } && def.Value != -1 &&
                _services.Enchants.GetUseCommands(def.ResultSpellId)
                    .Count > 0 &&
                (def.ResultSpellChance <= 0 ||
                    Random.Shared.Next(100) < def.ResultSpellChance))
                await AddPlayerEnchantAsync(vsession, v, (int)st.CharId,
                    def.ResultSpellId,
                    def.Value + GetSpellAttrModify(st, 9, spellId),
                    lvl,
                    CancellationToken.None, hidden: def.Value == -9);
        }

        // Entités non-caractères du rayon (mobs) : seules les commandes
        // instantanées de l'enchant s'exécutent — IsMyFriend natif les
        // compte comme victimes mais elles n'engagent jamais de
        // combat (pas de Hit*Skill, pas de vfunc+0x94).
        if (radius > 0 && def.ResultSpellId > 0)
            foreach (var mob in _services.Live.MobsOf(st.NodeId,
                st.InstanceGroup).ToList())
            {
                if (!mob.Alive || mob.IsElfPet) continue;
                var dx = mob.X - cx; var dz = mob.Y - cz;
                if (dx * dx + dz * dz > r2) continue;
                await ApplySpellResultCommandsAsync(session, st,
                    spellId, def, mob, CancellationToken.None);
            }
    }

    /// <summary>Vrai si l'enchant `eid` est HOSTILE au sens
    /// `OnBeAttackEnchant` natif — les commandes d'usage portent un
    /// effet d'attaque (locks 2055, DoT 3002, float/spell-attack, hate
    /// reset) ou une stat Attr* en valeur négative (debuff). Un buff
    /// pur (Attr* positif, soin, regen) ne provoque pas.</summary>
    private bool EnchantIsHostile(int eid)
    {
        foreach (var c in _services.Enchants.GetUseCommands(eid))
        {
            // Attaques d'effet direct — la cible est frappée par
            // l'application elle-même (Weak, floats, SpellAttack,
            // multi-stroke, hate, locks d'état, DoT).
            if (c.CommandId is 2 or 1120 or 1121 or 1997 or 1999
                or 2000 or 1031 or 2055 or 3002)
                return true;
            // Debuff de stat : commande Attr* (famille 20xx-29xx) avec
            // un premier argument négatif.
            if (c.CommandId is >= 2000 and < 3000 &&
                c.Args.Length > 0 && c.Args[0] < 0)
                return true;
        }
        return false;
    }

    /// <summary>Sort d'impact en zone (ResultSpellId) à la position
    /// courante du lanceur : dégâts + aggro + stun 4 s des mobs à moins
    /// de 5u. Réutilise la même famille de paquets que le sort ciblé
    /// (280 attrs + 281 hit + enchaînement riposte/mort).</summary>
    private async Task ApplyNowhereResultAsync(NetSession session,
        EntryState st, SpellParser.SpellDef dashDef,
        CancellationToken token)
    {
        var rdef = _services.Spells.GetDefinition(dashDef.ResultSpellId);
        // Dégâts par mob — pipeline natif `HitMagicSkill` (chaque cible
        // a ses propres rolls hit/crit/variance/résist, comme le natif
        // qui résout l'impact par entité touchée par le blast).
        var spLvlAoE = await PlayerSpellLevelAsync(st,
            dashDef.ResultSpellId);
        foreach (var mob in _services.Live.MobsOf(st.NodeId, st.InstanceGroup))
        {
            if (!mob.Alive || mob.IsElfPet) continue;
            var dx = mob.X - st.PosX; var dz = mob.Y - st.PosZ;
            if (dx * dx + dz * dz > 25f) continue;   // 5u de rayon
            var first = !mob.InCombat ||
                mob.TargetCharId != (int)st.CharId;
            BeginMobCombat(st, mob);
            mob.LastHitAt = DateTime.UtcNow;
            mob.StunnedUntil = DateTime.UtcNow.AddSeconds(4);
            st.InCombat = true;
            var sres = rdef != null
                ? NativeCombatFormulas.ResolveSpell(PlayerSnapshot(st),
                    MobSnapshot(mob), rdef,
                    SpellRefCoef(rdef, spLvlAoE),
                    CombatParams(), Random.Shared,
                    k => GetSpellAttrModify(st, k, rdef.Id))
                : new PhysicalResult(0, PhysicalOutcome.Hit);
            var dmg = SpellDamageMod(st, dashDef.ResultSpellId,
                sres.Damage);
            mob.Hp = Math.Max(0, mob.Hp - dmg);
            mob.SkillHitCharIds.Add((int)st.CharId);
            await RegisterBpDamageAsync(session, st, mob, rdef, dmg);
            await SendAttrAsync(session, mob.Handle, 0, mob.Hp, mob.Hp,
                token);
            await SendAttrAsync(session, mob.Handle, 0x13, mob.Patience, 0, token);
            await SendSpellHitAsync(session, dashDef.ResultSpellId,
                mob.Handle, st.CharId, dmg,
                NativeCombatFormulas.SpellHitFlagsOf(sres), token);
            // Procs « attaque » par victime du dash-AoE (miss exclu).
            if (sres.Outcome != PhysicalOutcome.Miss)
                await RunItemProcsAsync(session, st, mob,
                    ProcHitEvent.Attack, dmg, token,
                    NativeCombatFormulas.SpellHitFlagsOf(sres));
            if (mob.Hp <= 0)
                await KillMobAsync(session, st, mob, token,
                    dashDef.ResultSpellId, dmg);
            else if (first)
                StartMobCombatLoop(session, st, mob);
        }
    }
}
