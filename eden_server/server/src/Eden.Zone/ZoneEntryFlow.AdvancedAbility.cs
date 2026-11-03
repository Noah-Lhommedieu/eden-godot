using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Aptitudes avancées (arbre de talents de classe) —
/// CNE_CZ_ZoneServerSetAAPoint (148) / SetAARate (149).
/// Source native : CCharacter::AddAAEffectPoint @0x841ebb0
/// (server_us/ZoneServer).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Coût en aa_point du passage au niveau N d'un effet —
    /// table native <c>lapis::AARequestTable</c> @0x888c140 :
    /// index = niveau visé (0 = racine), niveaux 1-5 → 1 pt,
    /// 6-15 → 2 pts, 16-20 → 3 pts.</summary>
    private static readonly int[] AARequestTable =
    {
        0, 1, 1, 1, 1, 1,                 // paliers 0-5
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2,     // paliers 6-15
        3, 3, 3, 3, 3,                   // paliers 16-20
    };

    /// <summary>Cumul des coûts — table native
    /// <c>lapis::AARequestTableTotal</c> : coût total dépensé pour
    /// atteindre le niveau N (somme AARequestTable[1..N]), indexé par
    /// les points investis dans le talent.</summary>
    private static readonly int[] AARequestTableTotal = BuildAaTotal();

    private static int[] BuildAaTotal()
    {
        var t = new int[AARequestTable.Length];
        for (var i = 1; i < t.Length; i++)
            t[i] = t[i - 1] + AARequestTable[i];
        return t;
    }

    /// <summary>SetAAPoint (148) — wire `[s16 effectId][s16 classId]`
    /// (désasm Deserialize @0x8284e10 : deux s16 à +0x12/+0x14).
    /// Sémantique AddAAEffectPoint @0x841ebb0 : classId = la classe
    /// dont on pioche le pool aa_point dans player_classlist
    /// (0 = classe courante, char+0xa1c). Chaque appel n'ajoute
    /// qu'UN point (local_56 = cur+1) ; le coût vient de
    /// AARequestTable[cur+1]. Validations natives :
    /// l'entrée S_Talent doit avoir cost!=0 (byte+7) et maxPoint!=0
    /// (byte+2), le pré-requis preEffect doit avoir >= prePoint
    /// (byte+6), et le pool doit couvrir le coût. Émissions :
    /// 471 ClientUpdateAdvancedAbility [u8 type][u16 n][s16 id][s16 pt]
    /// (type 0 = classe courante, 2 = autre classe), attr 0x31
    /// (aa_point courant) pour la classe active, ou 489
    /// PlayerClassList pour une autre classe.</summary>
    private async Task HandleSetAaPointAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4 || st.CharId == 0) return;
        // s16 signé natif : les ids S_Talent ≥ 0x8000 arrivent en
        // négatif sur le fil ET sont stockés en s16 côté natif — on
        // compare donc les ids de la table tronqués en s16, comme le
        // natif (clé advanced_ability = s16).
        var effectId = BitConverter.ToInt16(payload, 0);   // s16 : effet
        var classId = BitConverter.ToInt16(payload, 2);    // s16 : classe (0=courante)
        if (effectId == 0) return;
        var targetClass = classId != 0 ? (int)classId : st.ClassId;

        // S_Talent : les 3 branches sont les lignes
        // (raceBase*100 + classId)*10 + {1,2,3} avec
        // raceBase = ((byte)char+0x4aa >> 4) >> 1 = race-1
        // (AddAAEffectPoint @0x841ebb0 : iVar11*10+b) — humain(1)→0,
        // zumi(2)→100, anuran(3)→200, ursun(4)→300, halfkin(5)→400.
        var treeBase = (st.RaceId - 1) * 100 + targetClass;
        var effects = await _services.AdvancedAbilities
            .GetEffectsAsync((int)st.CharId);
        var cur = effects.GetValueOrDefault(effectId);
        // Multi-disposition : la table CANONIQUE est C_Talent Reforged
        // (lot 12 — le client joué ; 495/915 arbres diffèrent de
        // S_Talent — ex. clerc arbre 41 : 54813 « 20 0 0 1 0 » chez
        // Reforged vs « 10 0 0 1 25 » côté serveur). Le paquet ne
        // porte que l'effectId — on évalue les règles natives de
        // CHAQUE variante et on accepte si l'une d'elles passe (le
        // client projet, qui lit l'équivalent de S_Talent, reste
        // jouable).
        GameDataParsers.TalentSlot? slot = null;
        var refuse = "slot introuvable";
        foreach (var trees in _services.Talents.BranchSets(treeBase))
        {
            var c = trees.SelectMany(t => t.Slots).FirstOrDefault(
                // SpellId des .ini est un int positif (ex. 52640) —
                // tronqué en s16 pour matcher le wire/DB natif.
                s => (short)s.SpellId == effectId);
            if (c == null) continue;
            var r = EvaluateAaSlot(c, trees, effects, cur);
            if (r == null) { slot = c; break; }
            refuse = r;
        }
        if (slot == null)
        {
            // Refus silencieux natif — journalisé pour diagnostic
            // live (talent introuvable = mauvais treeBase/classe).
            EdenLog.Write($"[Zone] SetAAPoint refus char={st.CharId} " +
                $"eff={effectId} class={targetClass} " +
                $"treeBase={treeBase} : {refuse}");
            return;
        }
        var maxPoint = slot.Params[0];   // u8 +2 : maxPoint

        // Coût natif : 1er point = 1 fixe (local_5c=1 à l'insertion
        // map) ; sinon AARequestTable[cur+1].
        var cost = cur <= 0 ? 1 : AARequestTable[
            Math.Clamp(cur + 1, 0, AARequestTable.Length - 1)];
        // AddAAEffectPoint natif : classId==0 puise dans
        // player_characters.aa_point (char+0xa1c — pool de la classe
        // courante) ; classId!=0 puise dans player_classlist de la
        // classe ciblée (SCharacterClass+0xc).
        var rest = classId == 0
            ? await _services.Classes.TrySpendCurrentAaPointAsync(
                (int)st.CharId, cost)
            : await _services.Classes.TrySpendAaPointAsync(
                (int)st.CharId, targetClass, cost);
        if (rest < 0)
        {
            // Pool insuffisant — le natif appelle le callback
            // vtable+0x58 avec le texte 0x324 (@0x841ed4b/0x841f4b5).
            await SendTextIndexAsync(session, 0x324, token);
            return;
        }

        var newPoint = (short)(cur + 1);
        await _services.AdvancedAbilities.SetEffectPointAsync(
            (int)st.CharId, effectId, newPoint);

        // AddAAEffectPoint natif : pour la classe COURANTE l'effet
        // est appliqué immédiatement via EffectToLife(char, char,
        // effectId, -2, 0x1000) — enchant permanent dont le niveau
        // suit les points. (Classe tierce : seul le pool est
        // décrémenté — l'effet s'active au switch.)
        if (classId == 0)
        {
            // flags=0x1000 natif → IsHideIcon → enchant discret :
            // suivi en mémoire (reset Memory Tome) mais JAMAIS de
            // 299/icône ni de ligne player_enchants (la stat vient du
            // stage « talents » = args × points).
            await AddPlayerEnchantAsync(session, st, (int)st.CharId,
                effectId, -2, newPoint, token, hidden: true,
                persist: false);
            await ProfileStatEmitter.SendForCharacterAsync(session,
                _services, (int)st.CharId, token);
        }

        // 471 ClientUpdateAdvancedAbility — corps natif
        // [u8 UpdateType][u16 n][s16 effectId][s16 point]×n
        // (Serialize @0x81ed060). type 0 = classe courante,
        // type 2 = autre classe (ctor calls @0x841efd5/0x841f680).
        var body = new List<byte>
        {
            (byte)(classId == 0 ? 0 : 2),              // u8 : UpdateType
        };
        body.AddRange(BitConverter.GetBytes((short)1)); // u16 : n=1
        body.AddRange(BitConverter.GetBytes(effectId)); // s16 : effectId
        body.AddRange(BitConverter.GetBytes(newPoint)); // s16 : point
        await session.SendPacketAsync(471, body.ToArray(), token);

        if (classId == 0)
            // Classe courante : attr 0x31 (aa_point) — GainAAPoint
            // natif émet ClientUpdateAttr(0x31, char+0xa1c).
            await SendAttrAsync(session, (uint)st.CharId, 0x31,
                rest, rest, token);
        else
            // Autre classe : le natif réémet ClientPlayerClassList
            // pour la classe modifiée — notre 489 renvoie la liste
            // complète (superset accepté par le client).
            await HandleQueryClassListAsync(session, st, token);

        EdenLog.Write($"[Zone] SetAAPoint char={st.CharId} " +
            $"effect={effectId} class={targetClass} {cur}->{newPoint} " +
            $"aa_rest={rest}");
    }

    /// <summary>Évalue les conditions natives d'un slot de talent
    /// (AddAAEffectPoint @0x841ebb0) dans UNE disposition d'arbres :
    /// null = accepté, sinon une raison de refus pour le journal.
    ///   · byte+2 (maxPoint) ≤ 0 ou déjà atteint → refus silencieux
    ///   · gate de palier (@0x841f1xx) : si byte+7 (tier) et byte+8
    ///     (reqPts) non nuls, la somme des coûts cumulés
    ///     AARequestTableTotal[points] de tous les slots des 3 branches
    ///     dont byte+7 ≤ palier cible doit atteindre reqPts — le
    ///     « X points dans l'arbre » affiché par l'UI.
    ///   · pré-requis : advanced_ability[s16 byte+4] ≥ byte+6.</summary>
    private static string? EvaluateAaSlot(
        GameDataParsers.TalentSlot slot,
        IReadOnlyList<GameDataParsers.TalentTree> trees,
        IReadOnlyDictionary<short, short> effects, int cur)
    {
        var maxPoint = slot.Params[0];   // u8 +2 : maxPoint
        var preEffect = slot.Params[1];  // s16 +4 : effet requis
        var prePoint = slot.Params[2];   // u8 +6 : points requis
        var groupTier = slot.Params[3];  // u8 +7 : palier de l'arbre
        var reqPts = slot.Params[4];     // u8 +8 : total requis
        if (maxPoint <= 0 || cur >= maxPoint)
            return $"max {cur}>={maxPoint}";
        if (groupTier != 0 && reqPts != 0)
        {
            var spent = 0;
            foreach (var s in trees.SelectMany(t => t.Slots))
            {
                if (s.SpellId == 0 || s.Params[3] > groupTier)
                    continue;
                var pts = effects.GetValueOrDefault((short)s.SpellId);
                if (pts > 0)
                    spent += AARequestTableTotal[Math.Clamp(pts, 0,
                        AARequestTableTotal.Length - 1)];
            }
            if (spent < reqPts) return $"palier {spent}<{reqPts}";
        }
        if (prePoint > 0 &&
            effects.GetValueOrDefault((short)preEffect) < prePoint)
            return $"prerequis {preEffect}@" +
                $"{effects.GetValueOrDefault((short)preEffect)}" +
                $"<{prePoint}";
        return null;
    }

    /// <summary>SetAARate (149) — `[s16 rate]`. Le natif
    /// CCharacter::SetAARate @0x8409b70 est un STUB VIDE (feature
    /// inachevée côté serveur US) — on journalise seulement.</summary>
    private Task HandleSetAaRateAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length >= 2)
            EdenLog.Write($"[Zone] SetAARate char={st.CharId} " +
                $"rate={BitConverter.ToInt16(payload, 0)} (no-op natif)");
        return Task.CompletedTask;
    }

    /// <summary>Sync initiale de la map advanced_ability — émise avec
    /// la liste de classes à l'entrée. Même corps 471 type 0 que la
    /// mise à jour (le client applique chaque paire).</summary>
    private async Task SendAdvancedAbilityMapAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        // Map advanced_ability préchargée par le pipeline d'entrée
        // (Phase 2) — valide pour la première émission post-install ;
        // les mutations AA (SetAAPoint, reset) repassent en live.
        var effects = st.Entry is { Emitted: false,
                          AaEffects: not null } ae && !st.EntryDirty
            ? ae.AaEffects
            : await _services.AdvancedAbilities
                .GetEffectsAsync((int)st.CharId);
        if (effects.Count == 0) return;
        var body = new List<byte> { 0 };                 // u8 : UpdateType
        body.AddRange(BitConverter.GetBytes(
            (short)effects.Count));                      // u16 : n
        foreach (var (id, pt) in effects.OrderBy(kv => kv.Key))
        {
            body.AddRange(BitConverter.GetBytes(id));    // s16 : effectId
            body.AddRange(BitConverter.GetBytes(pt));    // s16 : point
        }
        await session.SendPacketAsync(471, body.ToArray(), token);
    }

    /// <summary>`CCharacter::ResetAA` @0x0841fca0 — reset de l'arbre
    /// de talents de la classe COURANTE (Memory Tome 40413/41467 →
    /// enchant 55149 → commande 6003 `CEC_ResetAA`, et texte
    /// `TC_ResetAdvancedAbility` @0x83bf730). Séquence native :
    /// 1. pour chaque effet de `advanced_ability` appartenant à l'une
    ///    des 3 branches de la classe courante : refund
    ///    `aa_point += AARequestTableTotal[point]` (coût cumulatif),
    ///    `RemoveEnchantByID`, delete DB — les effets d'autres
    ///    classes sont conservés sans refund.
    /// 2. 471 `ClientUpdateAdvancedAbility` UpdateType=1 (reset) avec
    ///    la copie de la map restante.
    /// 3. `ClientUpdateAttr` attr=0x31 = nouveau aa_point.
    /// Les enchants AA sont cachés (flags 0x1000 → IsHideIcon) : pas
    /// de 300 à émettre (le client n'a jamais eu l'icône) — on retire
    /// seulement l'état mémoire.</summary>
    private async Task ResetAdvancedAbilitiesAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var effects = await _services.AdvancedAbilities
            .GetEffectsAsync((int)st.CharId);
        var treeBase = (st.RaceId - 1) * 100 + st.ClassId;
        // Union sur les dispositions (serveur + client Reforged) :
        // un talent placé dans l'arbre par N'IMPORTE quelle variante
        // appartient à la classe pour le refund.
        var owned = _services.Talents.OwnedSpellIds(treeBase);
        if (owned.Count == 0) return;

        // 1. Refund + retrait par effet de la classe courante.
        var refund = 0;
        var removedAny = false;
        foreach (var (effectId, point) in effects)
        {
            if (point <= 0 || !owned.Contains(effectId)) continue;
            refund += point >= 0 && point < AARequestTableTotal.Length
                ? AARequestTableTotal[point]
                : AARequestTableTotal[^1];
            // RemoveEnchantByID natif — enchant caché : l'état
            // mémoire tombe sans paquet (jamais de 299 → pas de 300).
            // RollBack des mods AttrSpell* cumulés ×points
            // (CEC_*::RollBack boucle counter fois).
            await ExecuteSpellAttrCommandsAsync(session, st,
                effectId & 0xffff, -point, token);
            st.Statuses.Remove(effectId & 0xffff, out _);
            await _services.AdvancedAbilities.RemoveEffectAsync(
                (int)st.CharId, effectId);
            removedAny = true;
        }
        if (!removedAny) return;

        // Refund dans le pool courant (player_characters.aa_point,
        // borné [0,120] par AddAaPointAsync — GainAAPoint natif).
        var rest = await _services.Classes.AddAaPointAsync(
            (int)st.CharId, refund);
        if (rest < 0) rest = 0;

        // 2. 471 UpdateType=1 (reset) — la map restante (autres
        // classes conservées) est réémise en snapshot.
        var remaining = effects
            .Where(kv => kv.Value > 0 && !owned.Contains(kv.Key))
            .OrderBy(kv => kv.Key).ToList();
        var body = new List<byte> { 1 };             // u8 : UpdateType=1 reset
        body.AddRange(BitConverter.GetBytes((short)remaining.Count));
        foreach (var (id, pt) in remaining)
        {
            body.AddRange(BitConverter.GetBytes(id)); // s16 : effectId
            body.AddRange(BitConverter.GetBytes(pt)); // s16 : point
        }
        await session.SendPacketAsync(471, body.ToArray(), token);

        // 3. attr 0x31 = aa_point courant (ResetAA natif @0x084204xx).
        await SendAttrAsync(session, (uint)st.CharId, 0x31,
            rest, rest, token);
        await ProfileStatEmitter.SendForCharacterAsync(session,
            _services, (int)st.CharId, token);
        EdenLog.Write($"[Zone] ResetAA char={st.CharId} " +
            $"class={st.ClassId} refund={refund} aa_rest={rest} " +
            $"removed={effects.Count - remaining.Count}");
    }
}
