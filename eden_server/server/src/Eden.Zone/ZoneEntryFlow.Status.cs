using Eden.Zone.Combat.SpellEffects;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Système de statuts sur joueur — miroir de
/// `CEnchantMgr` + `ILifeEntity::AddEnchant`/`RemoveEnchant` natifs.
///
/// Modèle natif (zone/decompiled.c) :
/// - `CEffectFactory::EffectToLife` @0x84f1f40 appelle
///   `ILifeEntity::AddEnchant` @0x847e9xx quand l'effet a une durée —
///   le CEnchant (effect_id, caster, rest_time +0x20, sets +0x28,
///   counter +0x30, level +0x3c) vit dans le CEnchantMgr de l'entité.
/// - Notification : `CNC_CZ_ClientEnchantAdd` (299) corps **23B**
///   `[u32 handle][s16 effectId][i32 restSec][u8 serial]
///   [s16 counter(+0x30)][i32 flagBits(+0x28 &amp; 0x7c000000 &gt;&gt; 0x1a)]
///   [s16 level(+0x3c)][i32 counter2(+0x34)]` — Serialize
///   @0x81e9840, remplissage dans AddEnchant @0x847ebxx ; le serial
///   u8 = char+0x280 incrémenté par add (NextAction ici).
/// - Retrait : `CNC_CZ_ClientEnchantRemove` (300) corps **15B**
///   `[u32 handle][s16 effectId][u8 act][i32][4B]` (Serialize
///   @0x81e98d0).
/// - Persistance : `player_enchants` (Insert/Update/Delete Enchant
///   des SQL natifs) — restauration à la reconnexion.
/// - Tick : `CEnchantMgr::RestarupEvent` arme les événements
///   périodiques — commande S_Enchant **3001** (var3 = HP/seconde
///   rendu « 每秒恢復 N 點生命值 ») et **3002** (var1 signée, négatif
///   = drain/seconde — 荊棘咒縛 -10/s).
/// - Invincibilité de migration : flag `char+0x3c bit7` +
///   `OnMigrationInvincibleTimeout` @0x8408eb0 (pas un enchant) —
///   portée par `MigrationInvincibleUntil`.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Enchant actif sur un joueur (CEnchant natif).</summary>
    internal sealed class ActiveStatus
    {
        /// <summary>effect_id — clé de FindById native.</summary>
        public int EffectId;
        /// <summary>caster_id persisté (0 = source système).</summary>
        public int CasterId;
        /// <summary>rest_time en secondes (&lt;0 = infini, ex. -1/-2
        /// des EffectToLife natifs durée -2).</summary>
        public int RestSeconds;
        /// <summary>restSec PASSÉ à l'ajout — RestSeconds est décrémenté
        /// par le tick, ce champ garde la durée initiale pour les
        /// enchants chaînés (1921) dont le successeur repart à pleine
        /// durée.</summary>
        public int InitialRestSec;
        /// <summary>Enchant sans icône (gate natif `param_3 == -9` /
        /// `IsHideIcon` — AddEnchant @0x847ea90) : le 299 n'a jamais
        /// été émis, le 300 ne doit pas partir non plus.</summary>
        public bool Hidden;
        /// <summary>Niveau de l'enchant (+0x3c — palier du sort).</summary>
        public int Level = 1;
        /// <summary>HP nets appliqués chaque seconde (3001 var3 &gt;0
        /// = régén ; 3002 var1 signé = positif régén boss / négatif
        /// DoT). 0 = statut sans tick vital.</summary>
        public int HpPerTick;
        /// <summary>Enchants chaînés à l'expiration — commande 1921
        /// `EffectNextID chance nextId rest flag` (var2 = id suivant).</summary>
        public readonly List<int> NextOnExpire = new();
        /// <summary>Verrous d'action imposés par la commande 2055 —
        /// bits <see cref="StatusLocks"/> : les compteurs
        /// `CEC_State*` natifs (+0xb8..+0xc0 sur char+0x8c), agrégés
        /// par <see cref="ZoneEntryFlow.GetStateLocks"/>.</summary>
        public int LockFlags;
        /// <summary>Dernière persistance du rest_time (sauvegarde
        /// opportuniste à la déconnexion).</summary>
        public DateTime AddedAt = DateTime.UtcNow;
        /// <summary>Compteur d'ajouts du CEnchant natif (+0x30) —
        /// incrémenté à chaque Add (`CEnchantMgr::Add` @…, ligne
        /// `+0x30 += 1`), émis en s16 dans le 299 (golden : 1 au
        /// premier add).</summary>
        public int Counter;
        /// <summary>Bonus % d'EXP de l'enchant — commande 2911
        /// `ItemExpRateUp` var2 (CEC_ItemExpRateUp::Execute :
        /// player+0x508 += float(arg2)). Lu par GrantCharExpAsync —
        /// retombe à 0 à l'expiration (status retiré de la map).</summary>
        public int ExpRateBonus;
        /// <summary>Bonus % d'exp de classe — 2911 var4 (paires
        /// chance|taux : var1/var2 = EXP, var3/var4 = NP/CP —
        /// `2911|||100|30` = +30% CP des charmes Iris).</summary>
        public int CpRateBonus;
        /// <summary>Bonus % de CP d'éveil — 2912/2914
        /// `ItemWakenNpRateUp` var2.</summary>
        public int WakenCpBonus;
        /// <summary>Une ligne player_enchants a été écrite pour ce
        /// statut (au moins une fois) — au retrait, le
        /// `Delete Enchant` ne part que si c'est vrai : les enchants
        /// de session (procs flag 0x1000 natif, talents AA) n'ont
        /// jamais de ligne à supprimer.</summary>
        public bool Persisted;
        /// <summary>Cumul actif de l'enchant stackable — borné par
        /// S_Enchant col59 (SEffectData+0x12e natif, défaut 1) : le
        /// stage « buffs » du StatPipeline multiplie les commandes
        /// par ce compte (CEnchantMgr::Add/Reset @0x08569a50 : stats
        /// cumulées uniquement tant que counter &lt; max).</summary>
        public int StackCount = 1;
    }

    /// <summary>Verrous d'action des statuts — commande S_Enchant
    /// 2055, compteurs `CEC_State*` natifs (+0xb8..+0xc0 sur la
    /// struct char+0x8c) et codes `ClientUpdateAttr` associés.</summary>
    private static class StatusLocks
    {
        /// <summary>var1 → `StateNoMove` (+0xb8, attr 1002) — bloque
        /// le déplacement (Immobilize).</summary>
        public const int NoMove = 1;
        /// <summary>var2 → `StateNoAttack` (+0xba, attr 1003) — bloque
        /// l'attaque de base.</summary>
        public const int NoAttack = 2;
        /// <summary>var3 → `StateNoUseSkill Kungfu` (+0xbe, attr 1005)
        /// — bloque les skills physiques.</summary>
        public const int NoSkillKungfu = 4;
        /// <summary>var4 → `StateNoUseSkill Magic` (+0xbc, attr 1004)
        /// — bloque les sorts magiques.</summary>
        public const int NoSkillMagic = 8;
        /// <summary>var5 → `StateNoUseItem` (+0xc0, attr 1006) —
        /// bloque l'usage d'items.</summary>
        public const int NoUseItem = 16;
        /// <summary>Code attr → bit : (1002,NoMove)(1003,NoAttack)
        /// (1005,Kungfu)(1004,Magic)(1006,NoUseItem) — ordre natif
        /// des compteurs char+0x8c.</summary>
        public static readonly (short Attr, int Bit)[] AttrMap =
            { (1002, NoMove), (1003, NoAttack), (1005, NoSkillKungfu),
              (1004, NoSkillMagic), (1006, NoUseItem) };
    }

    /// <summary>Verrous agrégés du perso — OU des LockFlags de tous
    /// les statuts actifs (les compteurs natifs empilent ; le test
    /// natif est `cnt > 0`). Résultat publié dans
    /// <c>EntryState.StateLocks</c> (lecture atomique hors-boucle).</summary>
    private static int GetStateLocks(EntryState st)
    {
        var flags = 0;
        foreach (var s in st.Statuses.Values)
            flags |= s.LockFlags;
        st.StateLocks = flags;
        return flags;
    }

    /// <summary>Réémet les 5 attrs de verrou au client — après un
    /// add/remove le client grise/dégrise déplacement, attaque,
    /// sorts et items (valeur = nombre de statuts verrouillant,
    /// compteurs natifs empilés).</summary>
    private async Task SendStateLockAttrsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        foreach (var (attr, bit) in StatusLocks.AttrMap)
        {
            var cnt = 0;
            foreach (var s in st.Statuses.Values)
                if ((s.LockFlags & bit) != 0) cnt++;
            // CNC_CZ_ClientUpdateAttr : [u32 handle][s16 attr][i32 val]
            await SendAttrAsync(session, st.CharId, attr, cnt, cnt,
                token);
        }
    }

    /// <summary>Applique un enchant sur le joueur — `AddEnchant` natif :
    /// upsert dans le mgr, 299 au client + can-see, persistance.
    /// `restSec &lt; 0` = durée infinie (monture, statut permanent).
    /// `refreshStats` = faux pendant la restauration d'entrée (le burst
    /// de login couvre déjà les attributs — évite N réémissions).</summary>
    /// <summary>Commandes à effet INSTANTANÉ natif — exécutées à
    /// l'apply puis l'effet disparaît (aucun `CEnchant` persistant) :
    /// gate `EffectToLife` natif `effet+0x12c != 0` (commande gérée)
    /// reste faux pour un effet purement instantané — c'est le cas
    /// des dispels (1821/1822/1823/1824), soins instantanés
    /// (1001/1011/2033/2034), téléport/charge (1802/1803/1804/1807/
    /// 1809), revive (1811/1812), LifeFloat/SpellAttack (1997/1999/
    /// 2000). Un heal pur comme Cure 50028 (enchant 52028 = dispel
    /// 1824 seul) ne doit donc JAMAIS apparaître dans la liste de
    /// status (bug live « Cure en status »).</summary>
    private static readonly HashSet<int> InstantOnlyCommands = new()
    {
        1001, 1011,                       // HP / MP min max
        1802, 1803, 1804, 1807, 1809,     // DistanceGoto / summon
        1811, 1812,                       // RevivePlus / Revive
        1821, 1822, 1823, 1824, 1825,     // dispels / EnchantIncrease
        1997, 1999, 2000,                 // LifeFloat / SpellAttack
        2033, 2034,                       // DamageAddPlus (tag formule)
        2039, 2047,                       // HealPlus variantes
        // 2031/2032 MultiStroke retirés : commande gérée → le natif
        // crée un CEnchant persistant (SEffectData+0x12c != 0), qui
        // sert de source de proc passif (talents « +N % double hit »).
    };

    /// <summary>Vrai si l'enchant ne porte QUE des commandes
    /// instantanées (aucune commande gérée = `SEffectData+0x12c == 0`
    /// natif) — son application exécute les effets une fois sans
    /// créer de status.</summary>
    private bool IsInstantOnlyEnchant(int eid)
    {
        var cmds = _services.Enchants.GetUseCommands(eid);
        if (cmds.Count == 0) return true;   // rien à persister
        foreach (var c in cmds)
            if (!InstantOnlyCommands.Contains(c.CommandId))
                return false;
        return true;
    }

    /// <summary>Exécute les commandes instantanées d'un enchant sans
    /// créer de status : dispels `EnchantKills` 1823 (ids listés),
    /// `EnchantKillRandom` 1821 et `...Force` 1824 — sélection native
    /// dans <see cref="EnchantDispel"/> (désasm CEC_EnchantKill*). Les
    /// autres commandes instantanées (heal, téléport) sont couvertes
    /// par les chemins d'appel (HealPower, ApplySpellResultCommandsAsync).
    /// </summary>
    private async Task ExecuteInstantEnchantAsync(NetSession session,
        EntryState st, int eid, CancellationToken token)
    {
        foreach (var c in _services.Enchants.GetUseCommands(eid))
        {
            IReadOnlyList<int> victims;
            switch (c.CommandId)
            {
                case 1822:
                {
                    // CEC_EnchantCancel @0x852c470 — retire les enchants
                    // dont le type (S_Enchant c38) ∈ [a0,a1] et le grade
                    // (c39) ≤ a2, non indissipables ; jusqu'à deux
                    // triplets (miroir du handler mob).
                    var rm = new List<int>();
                    double CA(int i) => i < c.Args.Length ? c.Args[i] : 0;
                    for (var k = 0; k < 2; k++)
                    {
                        var mn = (int)CA(k * 3);
                        var mx = (int)CA(k * 3 + 1);
                        var fl = (int)CA(k * 3 + 2);
                        if (mn <= 0 || mx <= 0) continue;
                        if (mx < mn) (mn, mx) = (mx, mn);
                        foreach (var s in st.Statuses.Values)
                            if (_services.Enchants.GetTypeId(s.EffectId)
                                    >= mn &&
                                _services.Enchants.GetTypeId(s.EffectId)
                                    <= mx &&
                                _services.Enchants.GetGrade(s.EffectId)
                                    <= fl &&
                                !_services.Enchants.IsUndispellable(
                                    s.EffectId))
                                rm.Add(s.EffectId);
                    }
                    victims = rm;
                    break;
                }
                case 1823:
                    victims = EnchantDispel.KillList(c.Args);
                    break;
                case 1821 or 1824:
                {
                    var picked = new List<int>();
                    foreach (var rule in EnchantDispel.RulesFor(
                        c.CommandId, c.Args))
                        picked.AddRange(EnchantDispel.Pick(rule,
                            DispelCandidates(st), () =>
                                Random.Shared.Next(100),
                            Random.Shared.Next));
                    victims = picked;
                    break;
                }
                default:
                    continue;
            }
            foreach (var id in victims)
                await RemovePlayerEnchantAsync(session, st, id, token);
        }
    }

    /// <summary>Enchants actifs du joueur vus comme candidats de
    /// dissipation (flags S_Enchant col7/col8 + icône).</summary>
    private IEnumerable<DispelCandidate> DispelCandidates(EntryState st)
        => st.Statuses.Values.Select(s => new DispelCandidate(
            s.EffectId, _services.Enchants.IsDebuff(s.EffectId),
            _services.Enchants.IsUndispellable(s.EffectId), s.Hidden))
            .ToList();

    /// <summary>Buff dev de vitesse (ServerConfig.DevMoveSpeedBuffId) :
    /// enchant 2051|pct de session, caché (pas de 299), non persisté —
    /// reposé à chaque entrée de zone. Passe par le stage « buffs »
    /// → Movement() → 296. Id 0 = désactivé.</summary>
    private async Task ApplyDevMoveSpeedBuffAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var id = Eden.Core.Game.ServerConfig.DevMoveSpeedBuffId;
        if (id <= 0 || _services.Enchants.Get(id) == null) return;
        // Le bundle d'entrée ne connaît pas cet enchant de session.
        st.EntryDirty = true;
        await AddPlayerEnchantAsync(session, st, (int)st.CharId, id,
            -9, 1, token, refreshStats: true, hidden: true,
            persist: false);
    }

    private async Task AddPlayerEnchantAsync(NetSession session,
        EntryState st, int casterId, int effectId, int restSec,
        int level, CancellationToken token, bool refreshStats = true,
        bool hidden = false, bool persist = true,
        int? seedCounter = null, int? seedStack = null,
        int rideItemSerial = 0)
    {
        var eid = (int)(short)effectId & 0xffff; // id u16 natif
        // Enchant purement instantané (dispel/heal/…) — exécution
        // unique des commandes, AUCUN statut créé (gate natif
        // `effet+0x12c != 0` de EffectToLife @0x08060ec0).
        if (IsInstantOnlyEnchant(eid))
        {
            await ExecuteInstantEnchantAsync(session, st, eid, token);
            return;
        }
        // CEnchantMgr::Add @0x08569a50 — FindByType : même type
        // (col38) → rejet + index 0x5b9 si l'existant domine, sinon
        // remplacement (300) — voir EnchantService.TypeAddAllowed.
        // shortcut: partition flag-bits du natif ignorée (group 0).
        if (!_services.Enchants.TypeAddAllowed(eid, level,
                st.Statuses.ToDictionary(k => k.Key,
                    v => (int)v.Value.Level), out var replaced))
        {
            await SendTextIndexAsync(session, 0x5b9, token);
            return;
        }
        foreach (var cid in replaced)
            await RemovePlayerEnchantAsync(session, st, cid, token);
        // CEnchantMgr::Add natif @0x08569a50 — re-add du même
        // effect_id : SEffectData+0xd bit0 (S_Enchant col8 & 0x100) →
        // refus sec ; niveau inférieur au niveau actif → refus ;
        // sinon Reset → rest_time repart à la durée du recast
        // (timer au max), level mis à jour, compteur +0x30 ++ borné
        // à col59 (max stack — le cumul de stats n'existe que pour
        // un enchant stackable).
        if (st.Statuses.TryGetValue(eid, out var status))
        {
            if (_services.Enchants.BlocksReapply(eid) ||
                level < status.Level)
                return;
        }
        else
        {
            status = new ActiveStatus { EffectId = eid };
            st.Statuses[eid] = status;
        }
        // Niveau précédent — les commandes AttrSpell* s'appliquent
        // par incrément (talent AA : Execute × point investi ; un
        // re-add au même niveau ne re-compte pas).
        var prevLevel = status.Counter == 0 ? 0 : status.Level;
        status.CasterId = casterId;
        status.RestSeconds = restSec;
        status.InitialRestSec = restSec;
        status.Hidden = hidden;
        status.Level = Math.Max(1, level);
        status.AddedAt = DateTime.UtcNow;
        // CEnchant+0x30 natif : ++ si col59 < 2 ou counter < col59
        // (un enchant non stackable garde 1 cumul de stats ; la borne
        // plafonne le cumul des stackables). En restauration
        // (recover enchant) le compteur/stack PERSISTÉS sont repris
        // tels quels — pas de ++ : sans ça chaque migration re-addait
        // → icône à 2 dès le 1er portail et stats ×N des stackables.
        var maxStack = _services.Enchants.GetMaxStack(eid);
        if (seedCounter.HasValue)
        {
            status.Counter = Math.Max(1, seedCounter.Value);
            status.StackCount = Math.Clamp(
                Math.Max(1, seedStack ?? 1), 1, Math.Max(1, maxStack));
        }
        else
        {
            if (maxStack < 2 || status.Counter < maxStack)
                status.Counter++;
            status.StackCount = Math.Clamp(status.Counter, 1,
                Math.Max(1, maxStack));
        }

        // Commandes de l'enchant (blocs cmd+6 vars col 9+7k) — les
        // familles tick sont résolues ici, les commandes instantanées
        // restent au call-site (même découpage que le natif : Execute
        // à l'apply, OnTime au tick via RestarupEvent).
        status.HpPerTick = 0;
        status.LockFlags = 0;
        status.NextOnExpire.Clear();
        status.ExpRateBonus = 0;
        status.CpRateBonus = 0;
        status.WakenCpBonus = 0;
        foreach (var c in _services.Enchants.GetUseCommands(eid))
        {
            switch (c.CommandId)
            {
                case 3001 when c.Args.Length > 2 && c.Args[2] > 0:
                    // HoT — « 每秒恢復 $var3$ 點生命值 ».
                    status.HpPerTick += (int)c.Args[2];
                    break;
                case 3002 when c.Args.Length > 0 && c.Args[0] != 0:
                    // DoT signé — « 每秒損失 N 點 » (var1 négative) ou
                    // régén monstre (var1 positive).
                    status.HpPerTick += (int)c.Args[0];
                    break;
                case 1921 when c.Args.Length > 1 && c.Args[1] > 0:
                    // EffectNextID — l'enchant var2 remplace celui-ci
                    // à expiration (chaîne HoT → boost heal).
                    status.NextOnExpire.Add((int)c.Args[1]);
                    break;
                case 2911:
                    // ItemExpRateUp — CEC_ItemExpRateUp::Execute :
                    // player+0x506 += s16(arg1), +0x508 += float(arg2)
                    // ; les paires chance|taux portent var1/var2 =
                    // EXP, var3/var4 = NP/CP (Iris `2911|||100|30` =
                    // +30% CP, 55047 `|100|25|100|25` = +25%/+25%).
                    // Le taux affiché par le tooltip est la var PAIRE.
                    if (c.Args.Length > 1)
                        status.ExpRateBonus += (int)c.Args[1];
                    if (c.Args.Length > 3)
                        status.CpRateBonus += (int)c.Args[3];
                    break;
                case 2912:
                case 2914:
                    // ItemWakenNpRateUp — taux CP d'éveil (même
                    // structure paire chance|taux).
                    if (c.Args.Length > 1)
                        status.WakenCpBonus += (int)c.Args[1];
                    break;
                case 2055:
                    // Verrous d'état — DecodeEffectCommand natif
                    // émet, pour chaque var non nulle, la commande
                    // StateNo* correspondante : var1→StateNoMove
                    // (attr 1002), var2→StateNoAttack (1003),
                    // var3→StateNoUseSkill Kungfu (1005),
                    // var4→StateNoUseSkill Magic (1004),
                    // var5→StateNoUseItem (1006), var6→SetNonSneak.
                    // Stun = 2055|1 1 1 1 1 1, Immobilize = 2055|1.
                    for (var i = 0; i < Math.Min(5, c.Args.Length); i++)
                        if (c.Args[i] != 0)
                            status.LockFlags |= 1 << i;
                    break;
            }
        }

        // 299 ClientEnchantAdd — corps 23B natif (Serialize @0x81e9840,
        // ctor rempli dans ILifeEntity::AddEnchant @0x847ebxx) :
        // [i32 handle][s16 effect][i32 rest][u8 serial][s16 counter]
        // [i32 flagBits][s16 level][i32 counter2] — codec généré.
        var add = new CNC_CZ_ClientEnchantAdd
        {
            Field1 = (int)st.CharId,            // i32 : handle entité
            Field2 = (short)eid,                // s16 : effect_id S_Enchant
            Field3 = status.RestSeconds,        // i32 : rest_time (s)
            Field4 = st.NextAction(),           // u8  : char+0x280 serial
            // s16 : cumul AFFICHÉ par le client (« ×N » de l'icône) —
            // StackCount capé col59 (défaut 1), pas le compteur de
            // recast brut : le natif envoie CEnchant+0x30 qui ne croît
            // qu'au 1er add (le recast passe par CEnchantMgr::Reset
            // @0x8568c10, sans ++ — notre Counter interne continuait
            // d'incrémenter et l'icône affichait ×2,×3 sur un buff
            // non-stackable, bug live).
            Field5 = (short)status.StackCount,
            Field6 = 0,                         // i32 : (sets&0x7c000000)>>0x1a
            Field7 = (short)status.Level,       // s16 : CEnchant+0x3c
            Field8 = 0,                         // i32 : CEnchant+0x34
        };
        // Rafraîchit le bitmask agrégé (sur la boucle — publication
        // atomique pour les lectures hors-boucle).
        GetStateLocks(st);

        // Gate icône natif (ILifeEntity::AddEnchant @0x847ea90) : le
        // 299 n'est émis QUE si `(sets & 0x7c000000)==0 &&
        // !IsHideIcon()` — IsHideIcon @0x855efa0 = SEffectData+0x118&2
        // OU CEnchant+0x28 bit 0x1000. Les enchants de talent AA sont
        // appliqués avec flags=0x1000 (AddAAEffectPoint @0x841ebb0)
        // → icône cachée : pas de 299/broadcast, la stat coule par le
        // pipeline (buffs discrets — bug live « talents en status »).
        if (!hidden)
        {
            await session.SendAsync(add, token);
            // BroadCastToCanSee natif — les témoins voient l'icône.
            await BroadcastToNodeAsync(session, st, add, token);
            // 372 TeamMemberEnchant aux pairs locaux (icônes groupe).
            await NotifyTeamEnchantAsync(session, st, token);
        }

        // CEC_Ride (commande 2134) — Execute natif dans
        // CEnchantMgr::Add : re-pose l'état de ride complet —
        // char+0x7f0/+0x7f4 (RideStates + serial item), bit9 (302),
        // modèle (283 mask=2), barre de sorts (545). C'est AUSSI le
        // chemin de restauration : la migration/relog repasse par
        // l'add (player_enchants) → le ride se re-peuple. Sans ça le
        // client gardait le modèle 283 mais « pensait être à pied »
        // (icône/statut perdus au portail — bug live lot19).
        if (_services.Enchants.GetRideInfo(eid) is { } rideInfo)
            await ReapplyRideStateAsync(session, st, eid, rideInfo,
                rideItemSerial, token);

        // CEC_State*::Execute natif : chaque verrou incrémente son
        // compteur et notifie l'attr (1002-1006) — le client grise
        // déplacement/attaque/sorts/items tant que cnt > 0.
        if (status.LockFlags != 0)
            await SendStateLockAttrsAsync(session, st, token);

        // Persistance player_enchants : désactivée pour les enchants
        // de talents AA (`persist: false`) — leur stat est re-dérivée
        // de `advanced_ability` (stage « talents », args × points) et
        // une ligne persistée doublerait. Les buffs `-9` (hidden)
        // persistent, eux : le stage « buffs » les lit pour leurs
        // commandes de stats et le restore les remet en hidden.
        if (persist)
            await _services.PlayerEnchants.UpsertEnchantAsync(
                new PlayerEnchantEntity
                {
                    PlayerId = (int)st.CharId, EffectId = eid,
                    CasterId = casterId, RestTime = status.RestSeconds,
                    // counter = CEnchant+0x30 natif persisté — le
                    // recover enchant le relit (seed au restore).
                    Counter = status.Counter,
                    Level = status.Level, StackCount = status.StackCount,
                });
        // Une fois persisté, le retrait devra purger la ligne (un
        // re-add non persisté d'un enchant déjà en DB laisse la ligne
        // à nettoyer au remove).
        status.Persisted |= persist;
        // Registre des enchants de session (CEnchant flag 0x1000
        // natif : procs d'équipement, talents AA) — le stage « buffs »
        // du StatPipeline le lit en complément de player_enchants ;
        // un add persisté couvre déjà l'effet via la ligne DB.
        var sessionMap = _services.SessionEnchants.GetOrAdd(
            (int)st.CharId,
            _ => new System.Collections.Concurrent
                .ConcurrentDictionary<int, int>());
        if (persist) sessionMap.TryRemove(eid, out _);
        // Valeur packée : level | stacks<<16 — le stage « buffs »
        // multiplie les commandes par le cumul stackable (col59).
        else sessionMap[eid] = status.Level | (status.StackCount << 16);
        // Quêtes CheckBuff (escortes…) : miroir du compteur côté mission.
        NotifyQuestEnchant(st, eid, Math.Max(1, (int)status.StackCount));
        EdenLog.Write($"[Status] add char={st.CharId} enchant={eid} " +
            $"rest={status.RestSeconds} lvl={status.Level} " +
            $"stack={status.StackCount} persist={persist} " +
            $"hidden={hidden}");

        // Commandes d'invocation (2037/2052/2054/2072/2073) — l'Execute
        // natif tourne à l'apply de l'effet (BornPet/BornTotemPet) et
        // non au tick : spawn ici, comme les commandes instantanées.
        await ExecuteSummonEnchantCommandsAsync(session, st, eid, token);
        // CEC_AttrSpell*::Execute — modificateurs d'attr de sort
        // (kinds 0-8) + 548 au caster pour les kinds 0-2. Le delta
        // de niveau couvre le ×points des talents (InvokeAAEnchants
        // natif applique l'effet 1×/point).
        await ExecuteSpellAttrCommandsAsync(session, st, eid,
            status.Level - prevLevel, token);

        // NotifyRefreshAttr natif : les commandes à effet d'attribut
        // (AttrBase, AttrMaxHP*, AttrPhysicoDamage…) déclenchent la
        // réémission 296/492/493 — l'étape « buffs » du StatPipeline
        // lit player_enchants, l'upsert ci-dessus rend l'effet visible.
        if (refreshStats)
            await RefreshStatsAfterStatusAsync(session, st, token);
    }

    /// <summary>Refresh complet après mutation des statuts —
    /// NotifyRefreshAttr natif : réémission profil + synchronisation
    /// de l'état live (plafonds vitals et canaux Bonus* du snapshot
    /// combat, kinddata). Sans ça les buffs n'apparaissaient que sur
    /// l'icône 299, jamais dans les stats réelles.</summary>
    private async Task RefreshStatsAfterStatusAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var (maxHp, maxMp) = await ProfileStatEmitter
            .SendForCharacterAsync(session, _services, (int)st.CharId,
                token);
        if (maxHp > 0) st.PlayerMaxHp = maxHp;
        if (maxMp > 0) st.PlayerMaxMp = maxMp;
        if (st.PlayerHp > st.PlayerMaxHp) st.PlayerHp = st.PlayerMaxHp;
        if (st.PlayerMp > st.PlayerMaxMp) st.PlayerMp = st.PlayerMaxMp;
        // Canaux Bonus* du snapshot combat réel — licences, talents,
        // titres, buffs, monture (stages du StatPipeline).
        var contrib = await _services.StatPipeline.ResolveAsync(
            (int)st.CharId, token);
        PlayerSnapshot(st).ApplyContribution(contrib);
    }

    /// <summary>Retire un statut — `RemoveEnchant` natif : 300 au
    /// client + can-see + `Delete Enchant`.</summary>
    private async Task RemovePlayerEnchantAsync(NetSession session,
        EntryState st, int effectId, CancellationToken token)
    {
        var eid = (int)(short)effectId & 0xffff;
        if (!st.Statuses.Remove(eid, out var status)) return;
        EdenLog.Write($"[Status] remove char={st.CharId} " +
            $"enchant={eid} rest={status.RestSeconds} " +
            $"persisted={status.Persisted} hidden={status.Hidden}");
        GetStateLocks(st);   // recalcule le bitmask agrégé publié
        // Quêtes CheckBuff : le buff n'est plus tenu (compteur 0).
        NotifyQuestEnchant(st, eid, 0);
        // Le 300 natif n'est émis que si l'enchant avait son icône
        // (RemoveEnchant @0x847af50 : `!IsHideIcon && dur != -9`) —
        // un enchant caché n'a jamais eu de 299, pas de 300 non plus.
        if (!status.Hidden)
        {
            var rm = new CNC_CZ_ClientEnchantRemove
            {
                Field1 = (int)st.CharId,    // i32 : handle entité
                Field2 = (short)eid,        // s16 : effect_id
                Field3 = st.NextAction(),   // u8  : actionId
                Field4 = 0, Field5 = 0,     // i32 ×2 : +0x10/+0x14 natifs
            };
            await session.SendAsync(rm, token);
            await BroadcastToNodeAsync(session, st, rm, token);
            await NotifyTeamEnchantAsync(session, st, token);
        }
        // Registre session : toujours purgé (idempotent).
        if (_services.SessionEnchants.TryGetValue((int)st.CharId,
                out var smap))
            smap.TryRemove(eid, out _);
        // `Delete Enchant` natif — inconditionnel : purge aussi les
        // lignes ORPHELINES écrites par l'ancien save (qui upsertait
        // les statuts de session/procs persist:false) — sinon le
        // stage « buffs » du StatPipeline relisait la ligne à chaque
        // resolve et les stats survivaient à l'annulation/expiration
        // (bug live « cancel enlève l'icône, les stats restent »).
        await _services.PlayerEnchants.DeleteEnchantAsync(
            (int)st.CharId, eid);
        // RollBack des CEC_State* — les compteurs retombent à 0 :
        // le client déverrouille les actions grisées.
        if (status.LockFlags != 0)
            await SendStateLockAttrsAsync(session, st, token);
        // La mort de l'enchant source désinvoque le pet
        // (RemoveEnchantByID natif sur pet+0x3f0).
        await DespawnPetsAsync(st, -1, eid, token);
        // CEC_AttrSpell*::RollBack — retrait des modificateurs +
        // réémission 548 (kinds 0-2). Le natif boucle `counter` fois
        // (× level pour les talents AA cumulés par point).
        await ExecuteSpellAttrCommandsAsync(session, st, eid,
            -status.Level, token);
        // EffectNextID natif : à l'expiration (pas à l'annulation)
        // l'enchant suivant prend le relais — sa durée repart à la
        // durée initiale de la chaîne (RestSeconds est déjà à 0 ici :
        // repasser cette valeur créait un statut éternel).
        foreach (var next in status.NextOnExpire)
            await AddPlayerEnchantAsync(session, st, status.CasterId,
                next, status.InitialRestSec, status.Level, token,
                refreshStats: false, persist: status.Persisted);
        // Le retrait retire aussi la contribution de l'enchant (la
        // ligne player_enchants est déjà supprimée) → réémission.
        await RefreshStatsAfterStatusAsync(session, st, token);
    }

    /// <summary>NCID 85 — annulation d'enchant par clic droit sur
    /// l'icône (`CNE_CZ_ZoneServerEnchantCancel::Execute` natif
    /// @0x824bab0 → `CEffectFactory::CancelPlayerEnchant` @0x805fe00).
    /// Gardes natives : l'effet existe (table SEffectData), col 7
    /// bit0 « buff » requis, col 8 bit 0x10 « can't abandon » refusé,
    /// puis `CheckRideSpellBuff` @0x81023f0 — un enchant portant la
    /// commande ride (2134/2140) n'est annulable que si on le monte
    /// (tous ses buffs de ride actifs) → chemin démontage. Retrait
    /// réel : RemoveEnchantByID → 300 + can-see + delete DB +
    /// refresh stats. Les refus natifs ne notifient rien au client
    /// (return 0 silencieux) — on consomme le paquet sans réponse.</summary>
    private async Task HandleEnchantCancelAsync(NetSession session,
        EntryState st, int effectId, CancellationToken token)
    {
        var eid = (int)(short)effectId & 0xffff; // id u16 natif
        var charId = (int)st.CharId;
        var def = _services.Enchants.Get(eid);
        if (def == null)
        {
            EdenLog.Write($"[Status] cancel enchant {eid} inconnu " +
                $"char={charId}");
            return;
        }
        // Gardes CancelPlayerEnchant : pas un buff / non abandonnable.
        // Spec propriétaire : toute monture se retire au clic droit,
        // y compris les montures de combat « can't abandon » (col8
        // 0x10) — le statut de ride est exempté des gardes.
        if (_services.Enchants.GetRideInfo(eid) == null &&
            !_services.Enchants.CanCancelByClient(eid))
            return;
        // CheckRideSpellBuff natif : l'enchant porte une commande de
        // ride (2134 Ride / 2140 RideSpell) → annulable uniquement
        // monté ; l'annulation = le démontage complet.
        var isRide = _services.Enchants.GetRideInfo(eid) != null ||
            _services.Enchants.GetUseCommands(eid)
                .Any(c => c.CommandId is 2140 or 1122);
        if (isRide)
        {
            if (_services.RideStates.TryGetValue(charId, out var ride)
                && ride.EnchantId == eid)
            {
                await DismountAsync(session, st, charId, token);
                EdenLog.Write($"[Status] dismount par clic droit " +
                    $"char={charId} enchant={eid}");
            }
            return; // ride non actif → refus natif silencieux
        }
        // RemoveEnchantByID natif : sans instance active, retour
        // silencieux (pas de paquet).
        if (!st.Statuses.ContainsKey(eid)) return;
        await RemovePlayerEnchantAsync(session, st, eid, token);
        EdenLog.Write($"[Status] cancel enchant={eid} char={charId}");
    }

    /// <summary>Broadcast d'un paquet lié au perso aux autres
    /// sessions du node — équivalent
    /// <c>CZoneServer::BroadCastToCanSee</c> : mêmes (node,instance)
    /// ET le charId dans la vue du destinataire (tous les appelants
    /// diffusent des données de cette entité : 283 attrs, statuts,
    /// elf 470, invocations 438/383 — hors-vue le client n'a pas le
    /// modèle pour les afficher).</summary>
    private async Task BroadcastToNodeAsync(NetSession session,
        EntryState st, int ncid, byte[] body, CancellationToken token,
        uint? handle = null)
    {
        var h = handle ?? (uint)st.CharId;
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                !ost.Sent.ContainsKey(h)) continue;
            try { await s.SendPacketAsync((ushort)ncid, body, token); }
            catch { /* session morte */ }
        }
    }

    /// <summary>Variante typée — codec généré sérialisé UNE fois via
    /// <see cref="PreparePacket"/> puis la trame est enfilée brute par
    /// destinataire (<see cref="SendPreparedToAsync"/> applique le
    /// décalage d'opcode propre à chaque build client).</summary>
    private async Task BroadcastToNodeAsync(NetSession session,
        EntryState st, NetCommand command, CancellationToken token,
        uint? handle = null)
    {
        var h = handle ?? (uint)st.CharId;
        var (op, payload) = PreparePacket(command);
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                !ost.Sent.ContainsKey(h)) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Boucle de statuts — tick 1 s par perso
    /// (`CEnchantMgr::RestarupEvent`/`OnTime` natif) : décrémente
    /// rest_time, applique les HP/seconde (3001 HoT, 3002 DoT),
    /// expire → 300 + purge DB. Un échec de tick ne tue jamais la
    /// boucle (même politique que la regen).</summary>
    private Task StatusTickLoopAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        // Point d'entrée : arme le premier pas — chaque tick se
        // reschedule sur la boucle de zone (mutations HP/status sur
        // le fil unique, pas de tâche while+Delay par session).
        ScheduleOnLoop(TimeSpan.FromSeconds(1),
            _ => StatusTickStepAsync(session, st, token), token);
        return Task.CompletedTask;
    }

    /// <summary>Un tick de statuts — corps de l'ancienne boucle
    /// while, reschedule sur la boucle tant que la session vit.</summary>
    private async Task StatusTickStepAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        {
            try
            {
                // Cycle guerre territoire — transitions de phase
                // (CEventTerritory natifs) détectées au tick pour les
                // persos inscrits : fin → 516/562/566/547 + retour.
                await TerritoryWarTickAsync(session, st, token);
                if (!st.Migrated || st.Statuses.Count == 0)
                    goto Reschedule;

                // Tick vital — chaque statut armé applique son delta
                // (plafonné [0,max], DoT sans descendre sous 0 —
                // le natif ne tue pas par le tick, il laisse 1 HP...
                // en fait il peut tuer : on borne à ≥1 par sécurité
                // gameplay pour ne pas créer de mort sans 305).
                if (!st.Dead && st.PlayerHp > 0)
                {
                    var delta = 0;
                    foreach (var s in st.Statuses.Values)
                        delta += s.HpPerTick;
                    if (delta != 0)
                    {
                        var next = Math.Clamp(st.PlayerHp + delta, 1,
                            st.PlayerMaxHp);
                        if (next != st.PlayerHp)
                        {
                            st.PlayerHp = next;
                            await SendVitalHpAsync(session, st.CharId,
                                st.PlayerHp, st.PlayerMaxHp,
                                CancellationToken.None);
                        }
                    }
                }

                // Expiration — snapshot de la liste (RemoveEnchant
                // mute le dictionnaire + peut enchaîner un enchant).
                var expired = new List<int>();
                foreach (var kv in st.Statuses)
                    if (kv.Value.RestSeconds > 0 &&
                        --kv.Value.RestSeconds == 0)
                        expired.Add(kv.Key);
                foreach (var eid in expired)
                {
                    EdenLog.Write($"[Status] expire char={st.CharId} " +
                        $"enchant={eid}");
                    await RemovePlayerEnchantAsync(session, st, eid,
                        CancellationToken.None);
                }

                // Filet de sécurité registre : un enchant de session
                // (persist:false — procs flag 0x1000 natif) dont le
                // statut a disparu par une autre voie (mort, dispel,
                // changement de node) purgerait sinon ses stats pour
                // toujours dans le stage « buffs » du StatPipeline.
                // Si des entrées ont sauté, on réémet les stats — sans
                // ça la contribution orpheline restait affichée.
                if (_services.SessionEnchants.TryGetValue(
                        (int)st.CharId, out var smap))
                {
                    var stale = smap.Keys
                        .Where(eid => !st.Statuses.ContainsKey(eid))
                        .ToList();
                    foreach (var eid in stale)
                    {
                        smap.TryRemove(eid, out _);
                        EdenLog.Write($"[Status] purge registre " +
                            $"char={st.CharId} enchant={eid}");
                    }
                    if (stale.Count > 0)
                        await RefreshStatsAfterStatusAsync(session, st,
                            CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                EdenLog.Write(
                    $"[Status] tick erreur char={st.CharId} : {ex.Message}");
            }
        }
    Reschedule:
        // Re-schedule tant que la session vit (token annulé à la
        // déconnexion → l'événement expire sans effet).
        if (st.Migrated)
            ScheduleOnLoop(TimeSpan.FromSeconds(1),
                _ => StatusTickStepAsync(session, st, token), token);
    }

    /// <summary>Notification 372 `CNC_CZ_ClientTeamMemberEnchant` aux
    /// membres locaux de la team — miroir natif : le ctor remplit
    /// `[s16 serial char+0x288][i32 memberCharId][u8 n]` + la map
    /// triée `{s16 effectId, s16 counter(+0x30), s16 level(+0x3c),
    /// i32 restSec}` (Serialize @0x80f9xx / corps ctor
    /// NotifyNewMember @0x810caxx). Émis aux AUTRES membres locaux
    /// (BroadToTeamMemberFilter hors source) à chaque add/remove —
    /// le client groupe affiche les icônes de buff/debuff.</summary>
    private async Task NotifyTeamEnchantAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null) return;
        var locals = LocalTeamMembers(st, team);
        // Plus de pair local → personne à notifier (soi exclus comme
        // le filtre natif).
        if (!locals.Any(o => o.CharId != st.CharId)) return;

        var list = st.Statuses.Values.ToList();
        if (list.Count > 255) list = list.Take(255).ToList();
        list.Sort((a, b) => a.EffectId.CompareTo(b.EffectId));  // set<> trié natif
        var body = new List<byte[]>
        {
            BitConverter.GetBytes((short)st.NextAction()),      // s16 : serial char+0x288
            BitConverter.GetBytes((int)st.CharId),              // i32 : membre concerné
            new[] { (byte)list.Count },                         // u8  : nb d'enchants
        };
        foreach (var s in list)
        {
            body.Add(BitConverter.GetBytes((short)s.EffectId)); // s16 : effect_id
            body.Add(BitConverter.GetBytes((short)0));          // s16 : counter +0x30
            body.Add(BitConverter.GetBytes((short)s.Level));    // s16 : level +0x3c
            body.Add(BitConverter.GetBytes(s.RestSeconds));     // i32 : rest_time
        }
        var payload = Concat(body.ToArray());
        foreach (var ost in locals)
        {
            if (ost.CharId == st.CharId || ost.Session == null)
                continue;
            try
            {
                await ost.Session.SendPacketAsync(372, payload, token);
            }
            catch { /* session morte */ }
        }
    }

    /// <summary>Restauration des enchants persistés à l'entrée —
    /// miroir du `recover enchant` natif (player_enchants → 299 × n).
    /// Les lignes à rest_time écoulé sont purgées.</summary>
    private async Task RestorePlayerEnchantsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var rows = await _services.PlayerEnchants.GetEnchantsAsync(
            (int)st.CharId);
        // Les enchants de talents (advanced_ability) sont cachés
        // natifs (AddAAEffectPoint, flags=0x1000) et jamais persistés
        // dans player_enchants — leur stat vient du stage « talents »
        // (×points). Toute ligne persistée avec un effect_id AA est un
        // reste de l'ancien chemin : on la saute et on la purge
        // (sinon icônes 299 au login + double comptage des stats).
        var aa = await _services.AdvancedAbilities.GetEffectsAsync(
            (int)st.CharId);
        var aaIds = new HashSet<int>();
        foreach (var id in aa.Keys) aaIds.Add(id & 0xffff);
        foreach (var r in rows)
        {
            // Les durées finies pendant l'offline ne sont pas
            // rejouées — purge directe (le natif expire à la charge)
            // : la ligne est supprimée pour ne pas s'accumuler.
            if (r.RestTime == 0)
            {
                await _services.PlayerEnchants.DeleteEnchantAsync(
                    (int)st.CharId, r.EffectId);
                continue;
            }
            if (aaIds.Contains(r.EffectId & 0xffff))
            {
                await _services.PlayerEnchants.DeleteEnchantAsync(
                    (int)st.CharId, r.EffectId);
                EdenLog.Write($"[Status] char={st.CharId} purge " +
                    $"enchant de talent persisté effect={r.EffectId}");
                continue;
            }
            if (!await FilterRideRestoreAsync((int)st.CharId, r.EffectId))
                continue;
            // rest_time = -9 : enchant « durée propre » natif — le 299
            // n'est jamais émis (gate AddEnchant `param_3 != -9`) →
            // restauration sans icône, stats actives.
            // recover enchant natif : counter (CEnchant+0x30) et
            // stack persistés relus — seedés SANS ré-incrémenter,
            // sinon chaque migration ajoutait un cumul (icône ×2 et
            // stats ×N des stackables au 1er portail).
            await AddPlayerEnchantAsync(session, st, r.CasterId,
                r.EffectId, r.RestTime, r.Level, token,
                refreshStats: false, hidden: r.RestTime == -9,
                seedCounter: r.Counter, seedStack: r.StackCount);
        }
        if (rows.Count > 0)
            EdenLog.Write($"[Status] char={st.CharId} " +
                $"{st.Statuses.Count} enchant(s) restauré(s)");
    }
}
