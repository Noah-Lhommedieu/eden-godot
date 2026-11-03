using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Partiel combat — pipeline des procs d'équipement.
///
/// Modèle natif (désasm zone) : à l'équipement, `RefreshEQEffect`
/// @0x84132a0 applique chaque `useEnchant` (S_Item col23) de l'item
/// comme CEnchant via `CEffectFactory::EffectToLife` (flag 0x1000).
/// Au combat, `CPlayerFactory::AttackTrigger` @0x80f5710 appelle
/// `ILifeEntity::OnAttackEnchant` (attaquant) + `OnBeAttackEnchant`
/// (défenseur) — `CEnchantMgr::OnAttackEnchant` @0x856a320 itère les
/// enchants actifs et exécute leurs commandes du vecteur de
/// l'événement via `CEnchant::OnAttack` @0x8560f90.
///
/// S_Enchant (CEnchantQuery::Deserialize @0x85ecf00) :
///   col40 = slots de commandes déclenchés (indices 1-based dans les
///           4 blocs cmd+6vars cols 9-36 — déplacés dans le set
///           CEnchantData+0x38),
///   col41 = événement (→ SEffectData+0x10 ; dispatch
///           LoadAllEffectData @0x8394b10 : 1/2 → +0x44 attaque,
///           3/4 → +0x5c coup reçu, 5 → +0xec cible, 6/7 → +0x8c kill,
///           8/9 → +0x98 être-tué),
///   col42 = chance % (→ +0x115), col43 = durée s (→ +0x120).
///
/// Événements « ToSelf » (impairs 1/3/5) → le sous-effet se pose sur
/// le porteur ; « ToOpponent » (pairs 2/4/6→7,9) → sur l'adversaire.
/// Les procs adversaire→mob sont journalisés sans effet (le registre
/// d'enchants mob n'existe pas encore).
///
/// Commandes implémentées : MultiStroke 1031/2031/2032
/// (CEC_MultiStroke @0x8546210 — jusqu'à 2 frappes extra, chance
/// arg1/arg3 %, dégât = facteur arg2/arg4 % × dégât du coup) et
/// EffectNextID 1921 + commandes d'attribut (posées comme enchant
/// temporaire de durée col43 sur la cible native de l'event).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Type d'événement de combat évalué pour les procs.</summary>
    private enum ProcHitEvent { Attack, Hurt, Kill, BeKill }

    /// <summary>Évalue les procs d'équipement du joueur pour un
    /// événement de combat — point d'entrée unique appelé depuis les
    /// chemins mêlée joueur→mob (Attack), mob→joueur (Hurt) et la mort
    /// du mob (Kill). <paramref name="hitDamage"/> = dégât du coup
    /// ayant déclenché (base du MultiStroke).</summary>
    private async Task RunItemProcsAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, ProcHitEvent evt,
        int hitDamage, CancellationToken token, int hitFlags = 0)
    {
        foreach (var (enchantId, stacks) in ProcSources(st))
        {
            var proc = _services.Enchants.GetProc(enchantId);
            if (proc != null)
            {
                var match = evt switch
                {
                    ProcHitEvent.Attack => proc.IsAttackEvent,
                    ProcHitEvent.Hurt => proc.IsHurtEvent,
                    ProcHitEvent.BeKill => proc.IsBeKillEvent,
                    _ => proc.IsKillEvent,
                };
                // Roll col42 — le natif gate le sous-effet entier
                // sur ce %.
                if (match && (proc.Chance >= 100 ||
                    Random.Shared.Next(100) < proc.Chance))
                {
                    EdenLog.Write($"[Zone] Proc {enchantId} " +
                        $"evt={proc.Event} char={st.CharId} " +
                        $"mob={mob.Handle} dmg={hitDamage}");
                    foreach (var cmd in proc.Commands)
                        await ExecuteProcCommandAsync(session, st, mob,
                            proc, cmd, hitDamage, token, hitFlags,
                            stacks);
                }
            }
            // MultiStroke passif (col40/41 vides — talents « +N %
            // double/triple », certificats, buffs) : chaque commande
            // = un roll indépendant sur l'événement attaque.
            if (evt != ProcHitEvent.Attack) continue;
            foreach (var cmd in _services.Enchants
                         .GetAttackCommands(enchantId))
                await RunMultiStrokeAsync(session, st, mob, enchantId,
                    (int)cmd.Args[0], cmd.Args[1], (int)cmd.Args[2],
                    cmd.Args[3], hitDamage, token, hitFlags, stacks);
        }
    }

    /// <summary>Sources de procs d'attaque du joueur — union dédup
    /// par effect_id (spéc 2.4.2) : enchants d'équipement
    /// (`ProcEnchantIds` — useEnchant col23) puis statuts actifs par
    /// <c>AddedAt</c> (buffs, talents, certificats posés par
    /// AddPlayerEnchantAsync). <c>Stacks</c> = cumul actif
    /// (CEnchant+0x30 natif — multiplie chance et facteur du
    /// MultiStroke) ; 1 pour l'équipement.</summary>
    private IEnumerable<(int EnchantId, int Stacks)> ProcSources(
        EntryState st)
    {
        var seen = new HashSet<int>();
        if (st.ProcEnchantIds != null)
            foreach (var id in st.ProcEnchantIds)
                if (seen.Add(id)) yield return (id, 1);
        foreach (var s in st.Statuses.Values.OrderBy(s => s.AddedAt))
            if (seen.Add(s.EffectId))
                yield return (s.EffectId, Math.Max(1, s.StackCount));
    }

    /// <summary>Exécute une commande du sous-effet déclenché —
    /// MultiStroke = frappe(s) extra instantanée(s) sur le mob ;
    /// 1921 EffectNextID / commandes d'attribut = enchant temporaire
    /// posé sur la cible native de l'événement (self si event
    /// impair, opponent si pair — journalisé sans effet sur mob).</summary>
    private async Task ExecuteProcCommandAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob,
        EnchantService.ProcDef proc, EnchantService.UseCommand cmd,
        int hitDamage, CancellationToken token, int hitFlags = 0,
        int stacks = 1)
    {
        double Arg(int i) => i < cmd.Args.Length ? cmd.Args[i] : 0;
        switch (cmd.CommandId)
        {
            // MultiStroke — CEC_MultiStroke::Execute @0x8546210 :
            // 2 coups extra max, rolls SÉQUENTIELS (`rand%100 <
            // chance_i` — un échec stoppe la boucle, le 2ᵉ roll n'est
            // tenté que si le 1ᵉ passe), dégât = `facteur% × dégât du
            // coup`. Args `[chance1, facteur1, chance2, facteur2]` à
            // arg0..arg3 — corrélé 1:1 aux tooltips (`2031|15|50` =
            // « +15 % ») ; arg0 est BIEN la chance (le sauter lisait
            // chance=facteur et donnait 0-1 coup au lieu de 2).
            case 1031: case 2031: case 2032:
                await RunMultiStrokeAsync(session, st, mob,
                    proc.EnchantId, (int)Arg(0), Arg(1), (int)Arg(2),
                    Arg(3), hitDamage, token, hitFlags, stacks);
                break;
            // EffectNextID — pose un autre enchant sur la cible du
            // proc (args[1] = enchant id ; args[0] = chance %).
            case 1921:
                if (Arg(1) > 0 && (Arg(0) <= 0 ||
                    Random.Shared.Next(100) < Arg(0)))
                    await ApplyProcBuffAsync(session, st, mob, proc,
                        (int)Arg(1), token);
                break;
            // Commandes d'attribut/buff → enchant temporaire sur la
            // cible native (icône + stats via le StatPipeline « buffs »,
            // durée = col43 du proc).
            default:
                if (cmd.CommandId >= 2000 && cmd.CommandId < 3000)
                    await ApplyProcBuffAsync(session, st, mob, proc,
                        proc.EnchantId, token);
                break;
        }
    }

    /// <summary>Frappes extra du MultiStroke — port fidèle de
    /// `CEC_MultiStroke::Execute` @0x8546210 : boucle de 2 coups,
    /// chacun `rand%100 &lt; chance_i` (un échec <b>arrête</b> la
    /// chaîne — le triple-hit n'existe que derrière un double
    /// réussi), dégât = `facteur% × dégât du coup` appliqué à la
    /// cible (vfunc+0x94 natif). Wire natif = <b>un seul</b> 292
    /// ClientEffectReport `[i32 attaquant][i32 victime]
    /// [s16 enchant][s16 0][s16 n][n×i32 −dmg][i32 hitFlags&amp;0x10]`
    /// émis au porteur (NotifyClient) + BroadCastToCanSee — chaque
    /// coup réussi est une valeur <b>négative</b> du vecteur
    /// (l'ancien twin 272 field5=-1 ne jouait pas l'affichage proc).
    /// </summary>
    private async Task RunMultiStrokeAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int enchantId,
        int chance1, double factor1, int chance2, double factor2,
        int hitDamage, CancellationToken token, int hitFlags = 0,
        int stacks = 1)
    {
        // Corps 292 natif — les dégâts extra sont empilés dans un
        // vecteur i32 négatif, le n de Field5 = nombre de coups.
        var body = new List<byte>(4 + 4 + 2 + 2 + 2 + 8 + 4);
        void Push<T>(T v) where T : unmanaged
        {
            var b = new byte[System.Runtime.InteropServices.Marshal
                .SizeOf<T>()];
            System.Runtime.InteropServices.MemoryMarshal.Write(b, in v);
            body.AddRange(b);
        }
        var strokes = new List<int>(2);
        var cfg = Eden.Core.Game.ServerConfig.Current;
        for (var roll = 0; roll < cfg.MultiStrokeMaxHits; roll++)
        {
            // Stack natif : `sVar2 = CEnchant+0x30` multiplie chance
            // ET facteur quand > 0 (Execute @0x8546349).
            var chance = (int)Math.Round((roll == 0 ? chance1 : chance2) * stacks * cfg.MultiStrokeChanceMult);
            var factor = (roll == 0 ? factor1 : factor2) * stacks * cfg.MultiStrokeDamageMult;
            if (chance <= 0 || factor <= 0) break;
            // Roll natif : l'échec STOPPE la chaîne (break), pas de
            // saut indépendant au roll suivant.
            if (chance < 100 && Random.Shared.Next(100) >= chance)
                break;
            var extra = (int)(hitDamage * factor * 0.01);
            if (extra <= 0) break;
            mob.Hp = Math.Max(0, mob.Hp - extra);
            strokes.Add(extra);
            EdenLog.Write($"[Zone] MultiStroke char={st.CharId} " +
                $"mob={mob.Handle} +{extra} (hp={mob.Hp})");
            if (mob.Hp <= 0 || !mob.Alive) break;
        }
        if (strokes.Count == 0) return;
        // 292 [i32 atk][i32 mob][s16 enchant][s16 0][s16 n][n×i32 −dmg]
        // [i32 flags] — flags natif = hitFlags du coup &amp; 0x10.
        Push((int)st.CharId);
        Push((int)mob.Handle);
        Push((short)enchantId);
        Push((short)0);
        Push((short)strokes.Count);
        foreach (var d in strokes) Push(-d);
        // flags natif = le mot de flags du coup déclencheur & 0x10
        // (bit block, même valeur recopiée dans le report natif).
        Push(hitFlags & 0x10);
        await session.SendPacketAsync(292, body.ToArray(), token);
        await BroadcastToNodePeersAsync(session, st,
            new ProcEffectReport(body.ToArray()));
        if (mob.Hp <= 0 || !mob.Alive)
            await KillMobAsync(session, st, mob, token, 0, strokes[^1]);
    }

    /// <summary>Paquet brut 292 pré-construit pour le broadcast aux
    /// témoins (le corps est déjà sérialisé — le codec généré 292 est
    /// à taille fixe n=1, inutilisable pour le vecteur multi-coups
    /// natif).</summary>
    private sealed class ProcEffectReport(byte[] body) : NetCommand
    {
        /// <summary>Requis par le scan reflexif des codecs (tests
        /// golden) — corps vide, jamais émis tel quel.</summary>
        public ProcEffectReport() : this(Array.Empty<byte>()) { }
        public override int Opcode => 292;
        public override string Name => "CNC_CZ_ClientEffectReport";
        public override void Serialize(OutBinStream stream) =>
            stream.WriteBytes(body);
        /// <summary>Sortant uniquement — corps déjà sérialisé.</summary>
        public override void Deserialize(InBinStream input) { }
    }

    /// <summary>Pose le buff du proc sur la cible native — event
    /// impair (ToSelf) → le porteur via AddPlayerEnchantAsync
    /// (icône 299 + stats par le stage « buffs » du StatPipeline +
    /// expiration par le tick status) ; event pair (ToOpponent) →
    /// l'adversaire via <see cref="AddMobEnchantAsync"/> (registre
    /// mob 2.3.1 — 299 broadcast + tick d'expiration).
    /// </summary>
    private async Task ApplyProcBuffAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob,
        EnchantService.ProcDef proc, int effectId,
        CancellationToken token)
    {
        if (effectId <= 0 ||
            _services.GameData.Enchants.All(e => e.Id != effectId))
            return;
        // Enchant temporaire — non persisté (le proc n'existe que
        // pour la session de combat, comme le CEnchant natif flag
        // 0x1000 qui disparaît au re-log/RefreshEQsAttr).
        var rest = proc.DurationSec > 0 ? proc.DurationSec : 60;
        if (!proc.TargetsSelf)
        {
            if (mob == null || !mob.Alive) return;
            EdenLog.Write($"[Proc] debuff mob={mob.Handle} " +
                $"proc={proc.EnchantId} → enchant={effectId} rest={rest}");
            await AddMobEnchantAsync(session, st, mob, (int)st.CharId,
                effectId, rest, 1, hidden: false, token);
            return;
        }
        // Enchant temporaire — non persisté (le proc n'existe que
        // pour la session de combat, comme le CEnchant natif flag
        // 0x1000 qui disparaît au re-log/RefreshEQsAttr).
        EdenLog.Write($"[Proc] buff char={st.CharId} proc={proc.EnchantId} " +
            $"→ enchant={effectId} rest={rest}");
        await AddPlayerEnchantAsync(session, st, (int)st.CharId,
            effectId, rest, 1, token, persist: false);
    }

    /// <summary>Procs de l'attaquant mob — OnAttackEnchant natif
    /// tourne sur le CEnchantMgr de <b>l'attaquant</b> : un mob portant
    /// des enchants à déclencheur (buffs posés par d'autres entités,
    /// triggers d'élite) roule ses propres procs contre le joueur
    /// victime (events 1/2 — self=mob, opponent=joueur).</summary>
    private async Task RunMobAttackProcsAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int hitDamage,
        CancellationToken token, int hitFlags = 0)
    {
        if (mob.Enchants.Count == 0) return;
        foreach (var en in mob.Enchants.Values.ToArray())
        {
            var proc = _services.Enchants.GetProc(en.EffectId);
            if (proc == null || !proc.IsAttackEvent) continue;
            if (proc.Chance < 100 &&
                Random.Shared.Next(100) >= proc.Chance)
                continue;
            EdenLog.Write($"[Zone] Proc mob={mob.Handle} " +
                $"enchant={en.EffectId} evt={proc.Event} " +
                $"dmg={hitDamage}");
            foreach (var cmd in proc.Commands)
                await ExecuteMobProcCommandAsync(session, st, mob, proc,
                    cmd, hitDamage, token, hitFlags,
                    Math.Max(1, (int)en.StackCount));
            if (st.PlayerHp <= 0) return;
        }
    }

    /// <summary>Commande d'un proc mob — mêmes familles que le côté
    /// joueur : MultiStroke = coups extra sur la victime (le joueur),
    /// 1921/attributs = enchant posé sur self (mob) ou opponent
    /// (joueur) selon le bit natif.</summary>
    private async Task ExecuteMobProcCommandAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob,
        EnchantService.ProcDef proc, EnchantService.UseCommand cmd,
        int hitDamage, CancellationToken token, int hitFlags, int stacks)
    {
        double Arg(int i) => i < cmd.Args.Length ? cmd.Args[i] : 0;
        switch (cmd.CommandId)
        {
            case 1031: case 2031: case 2032:
                await RunMobMultiStrokeAsync(session, st, mob,
                    proc.EnchantId, (int)Arg(0), Arg(1), (int)Arg(2),
                    Arg(3), hitDamage, token, hitFlags, stacks);
                break;
            case 1921:
                if (Arg(1) > 0 && (Arg(0) <= 0 ||
                    Random.Shared.Next(100) < Arg(0)))
                    await ApplyMobProcBuffAsync(session, st, mob, proc,
                        (int)Arg(1), token);
                break;
            default:
                if (cmd.CommandId >= 2000 && cmd.CommandId < 3000)
                    await ApplyMobProcBuffAsync(session, st, mob, proc,
                        proc.EnchantId, token);
                break;
        }
    }

    /// <summary>Frappes extra d'un MultiStroke mob → joueur — même
    /// port que <see cref="RunMultiStrokeAsync"/> : rolls séquentiels
    /// (échec = break), dégât = facteur%×coup, report 292
    /// `[mob][player]…`. La mort du joueur enchaîne sur la séquence
    /// native via l'appelant (st.PlayerHp ≤ 0).</summary>
    private async Task RunMobMultiStrokeAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int enchantId,
        int chance1, double factor1, int chance2, double factor2,
        int hitDamage, CancellationToken token, int hitFlags, int stacks)
    {
        var body = new List<byte>(26);
        void Push<T>(T v) where T : unmanaged
        {
            var b = new byte[System.Runtime.InteropServices.Marshal
                .SizeOf<T>()];
            System.Runtime.InteropServices.MemoryMarshal.Write(b, in v);
            body.AddRange(b);
        }
        var strokes = new List<int>(2);
        for (var roll = 0; roll < 2; roll++)
        {
            var chance = (roll == 0 ? chance1 : chance2) * stacks;
            var factor = (roll == 0 ? factor1 : factor2) * stacks;
            if (chance <= 0 || factor <= 0) break;
            if (chance < 100 && Random.Shared.Next(100) >= chance)
                break;
            var extra = (int)(hitDamage * factor * 0.01);
            if (extra <= 0) break;
            st.PlayerHp = Math.Max(0, st.PlayerHp - extra);
            strokes.Add(extra);
            EdenLog.Write($"[Zone] MultiStroke mob={mob.Handle} " +
                $"char={st.CharId} +{extra} (hp={st.PlayerHp})");
            if (st.PlayerHp <= 0) break;
        }
        if (strokes.Count == 0) return;
        // 292 [i32 mob][i32 player][s16 enchant][s16 0][s16 n]
        // [n×i32 −dmg][i32 flags] — identique au sens joueur→mob.
        Push((int)mob.Handle);
        Push((int)st.CharId);
        Push((short)enchantId);
        Push((short)0);
        Push((short)strokes.Count);
        foreach (var d in strokes) Push(-d);
        Push(hitFlags & 0x10);
        await session.SendPacketAsync(292, body.ToArray(), token);
        await BroadcastToNodePeersAsync(session, st,
            new ProcEffectReport(body.ToArray()));
        await SendVitalHpAsync(session, st.CharId,
            Math.Max(0, st.PlayerHp), st.PlayerMaxHp, token);
    }

    /// <summary>Buff/debuff d'un proc mob — self → registre mob
    /// (AddMobEnchantAsync), opponent → joueur (AddPlayerEnchantAsync,
    /// non persisté comme le CEnchant temporaire natif).</summary>
    private async Task ApplyMobProcBuffAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob,
        EnchantService.ProcDef proc, int effectId,
        CancellationToken token)
    {
        if (effectId <= 0 ||
            _services.GameData.Enchants.All(e => e.Id != effectId))
            return;
        var rest = proc.DurationSec > 0 ? proc.DurationSec : 60;
        if (proc.TargetsSelf)
        {
            EdenLog.Write($"[Proc] buff mob={mob.Handle} " +
                $"proc={proc.EnchantId} → enchant={effectId} rest={rest}");
            await AddMobEnchantAsync(session, st, mob, (int)mob.Handle,
                effectId, rest, 1, hidden: false, token);
        }
        else
        {
            EdenLog.Write($"[Proc] debuff char={st.CharId} " +
                $"proc={proc.EnchantId} → enchant={effectId} rest={rest}");
            await AddPlayerEnchantAsync(session, st, (int)mob.Handle,
                effectId, rest, 1, token, persist: false);
        }
    }
}
