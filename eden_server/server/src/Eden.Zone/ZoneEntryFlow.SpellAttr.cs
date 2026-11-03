//  ZoneEntryFlow.SpellAttr.cs — modificateurs d'attribut de sort
//  (famille AttrSpell* des commandes S_Enchant).
//
//  Port des CEC_AttrSpell* natifs — chaque commande enchant porte
//  une liste de spellIds (vars 1-5, non nulles) et une valeur en
//  var6 (Init natif : le premier élément de la liste = la valeur,
//  les suivants = les ids — la liste des vars est empilée à
//  l'envers par DecodeEffectCommand, donc var6 d'abord) :
//
//    CEC_AttrSpellRange          kind 0 (cmd 2143) — Execute @0x8513920
//    CEC_AttrSpellRadius         kind 1 (cmd 2150) — Execute @0x8513410
//    CEC_AttrSpellCoolDown       kind 2 (cmd 2141) — Execute @0x8510f90
//    CEC_AttrSpellPhyCriRates    kind 3 (cmd 2096) — Execute @0x8512420
//    CEC_AttrSpellPhyCriDamages  kind 4 (cmd 2097) — Execute @0x8510xxx
//    CEC_AttrSpellMagicCriRates  kind 5 (cmd 2098/2157)
//    CEC_AttrSpellMagicCriDamages kind 6 (cmd 2099)
//
//  En sus, les arbres de dégâts par sort de `CCharFormula::SpellDamage`
//  (@0x83f74c0), alimentés par `CCharacter::AddSpellDamage`
//  (@0x8417560, arbre char+0x704 = flat `dmg + v`) et son pendant
//  taux (arbre char+0x720 = `dmg × (100 + rate) / 100`) :
//
//    CEC_AttrSpellDamage         kind 7 (cmd 2063) — flat +0x704
//    CEC_AttrSpellDamageRate     kind 8 (cmd 2064) — rate +0x720
//
//  Kinds 0-2 : après AddSpellAttrModify, le natif émet
//  `548 ClientUpdateSpellAttr [s16 kind][s16 spellId][i32 valeur]`
//  en NotifyClient (jamais broadcast) pour chaque spellId présent
//  dans la table S_Spell globale (lookup `CZoneServer::Ins+0x1b8`).
//    - kinds 0/1 : valeur = nouveau total du modificateur
//      (`GetSpellAttrModify` — 0 inclus, le `if ==0 → 0` du natif
//      revient au même).
//    - kind 2 : valeur = 0 si total==0, sinon
//      `CCharFormula::CoolDownTime` @0x83f6060 =
//      round((100 - stat%)*(base)*0.01) * (mod*0.01 + 1.0), min 5
//      quand base ≠ 0 (le stat haste char+0x8c+0xa4 n'est pas
//      encore modélisé → (100-stat)=100).
//  Kinds 3-6 : `AddSpellAttrModify` seulement — pas d'émission 548
//  (preuve : CEC_AttrSpellMagicCriDamages::Execute @0x8510xxx ne
//  construit aucune commande ; les mods cri sont consommés
//  côté formules de dégâts, pas visibles en UI).

using Eden.Core.Data;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>kind natif (0-6) ← nom de commande S_Enchant.
    /// Preuve : arg `AddSpellAttrModify(entity, kind, …)` dans
    /// chaque CEC_AttrSpell*::Execute (désasm ci-dessus).</summary>
    private static int SpellAttrKindOf(string name) => name switch
    {
        "AttrSpellRange" => 0,
        "AttrSpellRadius" => 1,
        "AttrSpellCoolDown" => 2,
        "AttrSpellPhyCriRates" => 3,
        "AttrSpellPhyCriDamages" => 4,
        "AttrSpellMagicCriRates" => 5,
        "AttrSpellMagicCriDamages" => 6,
        "AttrSpellDamage" => 7,
        "AttrSpellDamageRate" => 8,
        // AttrSpellDuration → CCharacter::AddSpellDuration @0x84174a0
        // (arbre char+0x734, delta FLAT sur la durée —
        // CCharFormula::SpellDuration @0x83f73e0 : base+0x84 + add).
        "AttrSpellDuration" => 9,
        _ => -1,
    };

    /// <summary>`GetSpellAttrModify(entity, kind, spellId)` natif —
    /// total signé (0 si absent).</summary>
    private static int GetSpellAttrModify(EntryState st, int kind,
        int spellId)
        => st.SpellAttrMods.TryGetValue((kind, spellId), out var v)
            ? v : 0;

    /// <summary>`AddSpellAttrModify` : applique +delta au total
    /// (kind, spellId).</summary>
    private static int AddSpellAttrModify(EntryState st, int kind,
        int spellId, int delta)
    {
        var key = (kind, spellId);
        var total = (st.SpellAttrMods.TryGetValue(key, out var v)
            ? v : 0) + delta;
        st.SpellAttrMods[key] = total;
        return total;
    }

    /// <summary>`CCharFormula::CoolDownTime` @0x83f6060 simplifié —
    /// le terme (100 - haste%) vaut 100 faute de stat modélisée ; le
    /// facteur enchant `(mod × 0.01 + 1.0)` est celui du kind 2.
    /// Min 5 ds quand la base est non nulle (garde native).</summary>
    internal static int SpellAttrCooldownDs(int mod, int baseDs)
    {
        if (baseDs <= 0) return baseDs;
        var cd = (int)((mod * 0.01 + 1.0) * baseDs);
        return cd < 5 ? 5 : cd;
    }

    /// <summary>Cooldown effectif du sort pour ce perso — base S_Spell
    /// × facteur du mod kind 2.</summary>
    private int EffectiveCooldownDs(EntryState st, int spellId,
        int baseDs)
        => SpellAttrCooldownDs(GetSpellAttrModify(st, 2, spellId),
            baseDs);

    /// <summary>Entrée en zone : rejoue les commandes AttrSpell* des
    /// talents AA de la classe courante — l'enchant natif est caché
    /// (pas de ligne player_enchants) et le stage « talents » du
    /// pipeline ne couvre que les stats directes ; les mods per-spell
    /// (range/radius/cooldown/crits/dégâts) sont réappliqués ici
    /// ×points (InvokeAAEnchants natif : 1×/point). Même filtre
    /// d'arbre que le stage « talents » — 3 branches
    /// (race-1)*100+class ×10 + {1,2,3}.</summary>
    private async Task ApplyTalentSpellAttrsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        // Map advanced_ability préchargée par le pipeline d'entrée
        // (Phase 2) — même fenêtre de validité que le 471 du 489 ;
        // mutations AA en session → requête live.
        var effects = st.Entry is { Emitted: false,
                          AaEffects: not null } ae && !st.EntryDirty
            ? ae.AaEffects
            : await _services.AdvancedAbilities
                .GetEffectsAsync((int)st.CharId);
        if (effects.Count == 0) return;
        var treeBase = (st.RaceId - 1) * 100 + st.ClassId;
        // Union serveur+variantes clients (Reforged a un C_Talent
        // rééquilibré) — un talent reconnu par l'une des tables doit
        // avoir ses mods appliqués.
        var owned = _services.Talents.OwnedSpellIds(treeBase);
        foreach (var (effectId, point) in effects)
            if (point > 0 && owned.Contains(effectId))
                await ExecuteSpellAttrCommandsAsync(session, st,
                    effectId & 0xffff, point, token);
    }

    /// <summary>Execute (times=+n) / RollBack (times=−n) des commandes
    /// AttrSpell* d'un enchant — appliqué depuis
    /// <see cref="AddPlayerEnchantAsync"/> /
    /// <see cref="RemovePlayerEnchantAsync"/> et au login pour les
    /// talents (<see cref="ApplyTalentSpellAttrsAsync"/>). `times` =
    /// nombre d'Execute natifs (level/delta pour les talents AA,
    /// 1 pour un enchant ordinaire). Émet 548 au caster seul pour les
    /// kinds 0-2 quand le sort existe dans S_Spell (même gate que le
    /// lookup de la table native).</summary>
    private async Task ExecuteSpellAttrCommandsAsync(NetSession session,
        EntryState st, int eid, int times, CancellationToken token)
    {
        if (times == 0) return;
        foreach (var c in _services.Enchants.GetUseCommands(eid))
        {
            var name = EnchantCommands.Names.TryGetValue(c.CommandId,
                out var n) ? n : null;
            if (name == null) continue;
            var kind = SpellAttrKindOf(name);
            if (kind < 0 || c.Args.Length < 6) continue;
            // var6 = valeur (liste inversée native) — double dans les
            // données (« -2.5 », « 1.5 ») ; les accumulateurs natifs
            // sont int → arrondi away-from-zero à l'application.
            var delta = (int)Math.Round(c.Args[5] * times,
                MidpointRounding.AwayFromZero);
            for (var i = 0; i < 5; i++)
            {
                var spellId = (int)c.Args[i];
                // Clé native u16 — les sorts >32767 (ex. 50827) sont
                // valides : la borne s16 excluait TOUS leurs mods.
                if (spellId is <= 0 or > ushort.MaxValue) continue;
                var total = AddSpellAttrModify(st, kind, spellId,
                    delta);
                // 548 — kinds 0-2 seulement (les cri ne notifient
                // pas), et seulement si le sort existe en donnée.
                if (kind > 2 ||
                    _services.Spells.GetDefinition(spellId) is not
                        { } def) continue;
                var val = kind == 2
                    ? (total == 0 ? 0
                        : EffectiveCooldownDs(st, spellId,
                            def.CooldownDs))
                    : total;
                await session.SendAsync(new CNC_CZ_ClientUpdateSpellAttr
                {
                    Field1 = (short)kind,     // s16 : attr-kind
                    Field2 = (short)spellId,  // s16 : spell_id
                    Field3 = val,             // i32 : total / cooldown final
                }, token);
            }
        }
    }
}
