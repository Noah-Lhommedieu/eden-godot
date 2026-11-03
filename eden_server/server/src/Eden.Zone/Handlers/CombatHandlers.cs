using Eden.Core.Game.Services;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Combat : attaque mêlée, sorts, mort/XP/loot, résurrection,
/// enchants de proc (S_Enchant.ini).
///
/// ⚠ MORT-VIVANT PARTIEL : les opcodes 5 (melee), 9/16/17/59 (sorts),
/// 13 (spell break), 20/44 (revive) sont interceptés par
/// <see cref="ZoneEntryFlow.TryHandleAsync"/> AVANT ce dispatcher — les
/// handlers ci-dessous pour ces opcodes ne s'exécutent JAMAIS. Restent
/// vivants : les echos enchant 85/127/128. Le combat réel est dans
/// ZoneEntryFlow.Combat.cs.
/// </summary>
public static class CombatHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        d.Register(() => new CNE_CZ_ZoneServerMeleeAttack(),
            async (session, msg, token) =>
            {
                var targetId = msg.TargetId;
                var slot = msg.AttackSlot;
                var target = s.Entities.Find(targetId);
                if (target == null || target.IsDead) return;

                // Stats de l'attaquant — TODO(phase reverse): charger les vraies
                // stats du personnage (CharacterStats + equipement) depuis la DB.
                var attacker = new CombatService.CombatStats
                {
                    Level = 60, AttackPower = 1500, Defense = 800, Agi = 300,
                };
                var result = s.Combat.PhysicalAttack(
                    attacker, target.Stats, session.CharacterId ?? 0, targetId);
                var hpLeft = s.Entities.ApplyDamage(targetId, result.FinalDamage);

                EdenLog.Write($"[Zone] MeleeAttack charId={session.CharacterId} " +
                    $"target={targetId} dmg={result.FinalDamage} crit={result.Critical} " +
                    $"hpLeft={hpLeft}");

                // Broadcast du résultat + HP de la cible aux joueurs de la zone
                await s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientMeleeAttack
                {
                    Field1 = (uint)(session.CharacterId ?? 0),
                    Field2 = (uint)targetId,
                    Field3 = result.FinalDamage,
                    Field4 = result.Critical ? 1 : 0,
                    Field5 = hpLeft,
                }, token);
                await s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientUpdateAttrNewHp
                {
                    Field1 = targetId,
                    Field2 = 0,
                    Field3 = hpLeft,
                }, token);

                if (target.IsDead)
                    await OnTargetKilled(s, session, target, targetId, token);
            });

        d.Register(() => new CNE_CZ_ZoneServerSpellCastToLife(),
            async (session, msg, token) =>
            {
                var spellId = msg.SpellId;
                var targetId = (int)msg.TargetId;
                var spell = s.Spells.GetDefinition(spellId);
                if (spell == null) return;
                var charId = session.CharacterId ?? 0;
                var target = s.Entities.Find(targetId);

                // TODO(phase reverse): vraies stats du lanceur depuis le personnage
                var caster = new CombatService.CombatStats
                {
                    Level = 60, MagicPower = 1200, AttackPower = 1500, Int = 400, Agi = 300,
                };
                var targetStats = target?.Stats ?? new CombatService.CombatStats();

                // --- Temps de cast (S_Spell col 26 ×100 ms ; vide =
                // instant — désasm ISpell::OnCast : SSpellData+0x38,
                // plancher 4ds si non nul). Séquence native :
                // 279 ClientSpellCastOK [s16 spell][i32 caster]
                // [i32 target][u8] → 278 ClientSpellCast (mêmes champs
                // + 2 floats de position) → délai → hit.
                var castMs = spell.CastTimeMs > 0
                    ? Math.Max(spell.CastTimeMs, 400) : 0;
                if (castMs > 0)
                {
                    await session.SendAsync(new CNC_CZ_ClientSpellCastOK
                    {
                        Field1 = (short)spellId,       // s16 : id sort
                        Field2 = charId,               // i32 : handle lanceur
                        Field3 = targetId,             // i32 : handle cible
                        Field4 = 0,                    // u8  : réservé (0 natif)
                    }, token);
                    await s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientSpellCast
                    {
                        Field1 = (short)spellId,       // s16 : id sort
                        Field2 = charId,               // i32 : lanceur
                        Field3 = targetId,             // i32 : cible
                        Field4 = 0,                    // u8  : réservé
                        Field5 = 0f,                   // f32 : X cible (0 si n/a)
                        Field6 = 0f,                   // f32 : Z cible
                    }, token);
                    await Task.Delay(castMs, token);
                    if (token.IsCancellationRequested) return;
                }

                if (spell.TargetType == 1 && target is { IsDead: false })
                {
                    // Sort offensif : dégâts = val% de la puissance (S_Spell col 41)
                    caster.MagicPower = caster.MagicPower * spell.Value / 100;
                    var result = s.Combat.SpellAttack(
                        caster, targetStats, session.CharacterId ?? 0, targetId);
                    var hpLeft = s.Entities.ApplyDamage(targetId, result.FinalDamage);
                    EdenLog.Write($"[Zone] Spell {spellId} ({spell.Name}) " +
                        $"dmg={result.FinalDamage} crit={result.Critical} hpLeft={hpLeft}");
                }
                else
                {
                    // Buff/soin — appliqué via SpellService (stacks, durée, ticks)
                    s.Spells.ApplySpell(spellId, session.CharacterId ?? 0, targetId);
                    EdenLog.Write($"[Zone] Buff {spellId} ({spell.Name}) -> target={targetId}");
                }
            });

        // Sorts de zone : CastToLocation [short spellId, float x, float y]
        d.RegisterChar(() => new CNE_CZ_ZoneServerSpellCastToLocation(),
            (session, msg, charId, token) =>
                CastArea(s, charId, msg.SpellId, msg.X, msg.Y));

        // CastToNearby [short spellId, float radius] — autour du lanceur
        d.RegisterChar(() => new CNE_CZ_ZoneServerSpellCastToNearby(),
            (session, msg, charId, token) =>
            {
                var self = s.Entities.Find(charId);
                return CastArea(s, charId, msg.Field1,
                    self?.X ?? 0, self?.Y ?? 0, msg.Field2);
            });

        // CastToNowhere [short spellId, float ?] — sort instant sans cible
        d.RegisterChar(() => new CNE_CZ_ZoneServerSpellCastToNowhere(),
            (session, msg, charId, token) =>
            {
                var spell = s.Spells.GetDefinition(msg.SpellId);
                if (spell != null)
                    s.Spells.ApplySpell(msg.SpellId, charId, charId);
                return Task.CompletedTask;
            });

        // SpellBreak — interruption du cast en cours
        d.RegisterChar(() => new CNE_CZ_ZoneServerSpellBreak(),
            (session, msg, charId, token) =>
            {
                // TODO(phase reverse): annuler le cast timer du personnage
                return Task.CompletedTask;
            });

        // Revive : ReviveType int : 0=point de rez, 1=sur place (cash), etc.
        d.RegisterChar(() => new CNE_CZ_ZoneServerRevive(),
            (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] Revive char={charId} type={msg.ReviveType}");
                // TODO(phase reverse): téléporter au revive_area de la scène
                // courante + restaurer HP/MP + pénalité XP (S_Parameter.ini)
                return Task.CompletedTask;
            });

        // Enchants de proc (S_Enchant.ini) : activation/pause/cancel d'un
        // enchant conditionnel (buff déclenché sur attaque/crit/being-hit)
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnchantStart(),
            async (session, msg, charId, token) =>
            {
                if (s.Enchants.Get(msg.Value) == null) return;
                await session.SendAsync(new CNC_CZ_ClientEnchantStart
                {
                    Field1 = charId, Field2 = msg.Value,
                }, token);
                EdenLog.Write($"[Zone] EnchantStart char={charId} enchant={msg.Value}");
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerEnchantPause(),
            async (session, msg, charId, token) =>
                await session.SendAsync(new CNC_CZ_ClientEnchantPause
                {
                    Field1 = charId, Field2 = msg.Value,
                }, token));

        // NCID 85 (EnchantCancel) : intercepté par
        // ZoneEntryFlow.TryHandleOnLoopAsync → CancelPlayerEnchant
        // natif (retrait réel runtime+DB, pas un simple écho 300).
    }

    /// <summary>Mort d'une entité : quêtes, XP (S_Monster col 14), drops.</summary>
    /// <summary>Applique un sort de zone aux entités dans le rayon.</summary>
    private static Task CastArea(ZoneServices s, int casterId, int spellId,
        float x, float y, float radius = 8f)
    {
        var spell = s.Spells.GetDefinition(spellId);
        if (spell == null) return Task.CompletedTask;
        var hits = 0;
        foreach (var e in s.Entities.FindInRange(x, y, radius))
        {
            if (e.EntityId == casterId) continue;
            if (spell.TargetType == 1)
            {
                var caster = new CombatService.CombatStats
                {
                    Level = 60, MagicPower = 1200 * spell.Value / 100,
                    AttackPower = 1500, Int = 400, Agi = 300,
                };
                var result = s.Combat.SpellAttack(caster, e.Stats,
                    casterId, e.EntityId);
                s.Entities.ApplyDamage(e.EntityId, result.FinalDamage);
                hits++;
            }
            else
            {
                s.Spells.ApplySpell(spellId, casterId, e.EntityId);
                hits++;
            }
        }
        EdenLog.Write($"[Zone] AoE spell={spellId} ({spell.Name}) " +
            $"@({x:F0},{y:F0}) r={radius:F0} -> {hits} cibles");
        return Task.CompletedTask;
    }

    private static async Task OnTargetKilled(ZoneServices s,
        NetSession session,
        ZoneEntityRegistry.ZoneEntity target, int targetId, CancellationToken token)
    {
        EdenLog.Write($"[Zone] Entité {targetId} tuée par charId={session.CharacterId}");
        if (session.CharacterId is not { } killer) return;

        s.Missions.NotifyEvent(killer, "MonsterKilled", target.MonsterId);
        var def = s.Monsters.GetDefinition(target.MonsterId);
        if (def == null) return;

        var ch = await s.Characters.GetCharacterByIdAsync(killer);
        if (ch != null)
        {
            var (newLevel, gained) = await s.Progression.GrantExpAsync(
                killer, def.ExpReward, ch.Level, ch.Experience);
            if (gained > 0)
                EdenLog.Write($"[Zone] char={killer} LEVEL UP -> {newLevel}");
            // Notification XP au client (int charId, int nouvelleXp)
            var updated = await s.Characters.GetCharacterByIdAsync(killer);
            if (updated != null)
                await session.SendAsync(new CNC_CZ_ClientUpdateAttrNewExp
                {
                    Field1 = killer,
                    Field2 = (int)updated.Experience,
                }, token);

            // --- XP de classe pour la classe active du tueur ---
            // S_Monster col 55 (1-idx) = NP (CMonsterData+0xa4) — valeur
            // distincte de l'exp perso. Persiste player_classlist et
            // rafraîchit la barre HUD : ClientUpdateAttr attr=6 =
            // class_point courant (désasm GainClassPoint @0841c24b,
            // wire natif [i32 id][s16 attr][i32 cur][i64 0] = 18B).
            if (def.ClassExp > 0 && ch.ClassId > 0)
            {
                await s.Classes.AddClassExpAsync(killer, ch.ClassId,
                    def.ClassExp);
                var cp = await s.Classes.GetClassProgressAsync(killer,
                    ch.ClassId);
                if (cp != null)
                {
                    // +0x10 i64 = 0 (natif)
                    await session.SendAsync(new CNC_CZ_ClientUpdateAttr
                    {
                        Field1 = (int)killer,           // u32 : handle
                        Field2 = 6,                     // s16 : attr class_point
                        Field3 = (int)Math.Min(int.MaxValue, cp.ClassExp),
                        Field4 = 0,
                    }, token);
                }
            }
        }
        var drops = s.Loot.RollClassDrop(def.Family);
        foreach (var loot in drops)
        {
            var touched = new List<(int BagId, int Slot, bool Merged)>();
            var (ok, _, bag, slot) = await s.Inventory.AddItemAsync(
                killer, loot.ItemId, loot.Quantity, 0, 0, false, touched);
            if (!ok)
            {
                // Inventaire plein → trésor au sol (CTreasure natif,
                // DropTreasureOnFloor) via le callback du flow.
                if (s.TreasureDrop is { } drop)
                    await drop(session, loot.ItemId, loot.Quantity);
                continue;
            }
            // Merge → 291, nouveau slot → 274 (règle native —
            // EmitItemSlotsAsync ; un 274 sur slot occupé crashe).
            await Eden.Zone.Codecs.NativeItemCodec.EmitItemSlotsAsync(
                session, s.InventoryRepo, killer, touched, token);
            // 146 — butin ramassé → ItemCollect.
            s.NotifyItemChanged(killer, loot.ItemId);
        }
        EdenLog.Write($"[Zone] +{def.ExpReward} XP, {drops.Count} drop(s)");
    }
}
