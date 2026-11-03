using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Titres / appellations — portage des NCIDs 93/94 et 396/397
/// (P13.2).
///
/// <c>CPlayerFactory::SetPresentAppellation</c> (zone @0x080f2400) :
///   - id 0 = retirer le titre (toujours autorisé) ; id > 0 exigé
///     dans le vector des titres possédés (char+0x6d8 =
///     `player_appellation`).
///   - ancien enchant retiré si le titre quitté n'est pas de type 2
///     (data+0x10 == 2) — or <c>CTitleQuery::Deserialize</c>
///     (@0x086361xx) fixe data+0x10 = 4 pour TOUS les titres : la
///     branche « enchant permanent à l'obtention » d'AddAppellation
///     (@0x08103a40) est donc du code mort natif — l'enchant du
///     titre n'existe QUE tant qu'il est actif. Notre étape stats
///     « titles » (Program.cs) ne somme que le titre actif.
///   - char+0x4df++ (seq u8), char+0x6fc = nouveau titre, 397
///     <c>ClientUpdateAppellation</c> [i32 titre] au joueur,
///     `SavePlayerCharacter "appellation"`, 396
///     <c>ClientChangeAppellation</c> [i32 char][i32 titre][u8 seq]
///     broadcast aux voyants (BroadCastToCanSee).
///
/// <c>CNE_CZ_ZoneServerQueryAppellation::Execute</c> (zone
/// @0x082748c0) : cible = char du même node (GetCharacter) — 396
/// [char][titre][seq] + 280 attr 0x2c (=char+0x47c, byte du bloc
/// player-card — approximé par le nombre de titres possédés).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>NCID 93 — pose/retrait du titre actif. Ordre natif :
    /// vérif possession → seq++/set → 397 → persist → 396 voyants →
    /// refresh de stats (le swap d'enchant).</summary>
    private async Task HandleSetAppellationAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        var titleId = payload.Length >= 2
            ? BitConverter.ToUInt16(payload, 0) : (ushort)0;
        if (titleId != 0)
        {
            // Id non possédé → rejet natif silencieux (la boucle de
            // recherche dans char+0x6d8 échoue → return).
            var owned = await _services.Characters
                .GetAppellationsAsync((int)st.CharId);
            if (!owned.Contains(titleId))
            {
                EdenLog.Write($"[Titles] char={st.CharId} titre " +
                    $"{titleId} non possédé — rejet");
                return;
            }
        }
        st.TitleSeq++;
        st.ActiveTitle = titleId;
        // 397 ClientUpdateAppellation [i32 titre] — NotifyClient au
        // joueur (piVar9[1] = u16 char+0x6fc élargi en i32).
        await session.SendAsync(new CNC_CZ_ClientUpdateAppellation
        { Value = titleId }, token);
        // SavePlayerCharacter "appellation" natif.
        await _persistence.EnqueueAsync((int)st.CharId,
            ct => _services.Characters.SetAppellationAsync(
                (int)st.CharId, titleId));
        // 396 [i32 char][i32 titre][u8 seq] — BroadCastToCanSee natif
        // (les voyants mettent à jour l'affichage au-dessus de la
        // tête).
        await BroadcastToNodeAsync(session, st,
            new CNC_CZ_ClientChangeAppellation
            {
                Field1 = (int)st.CharId,
                Field2 = titleId,
                Field3 = st.TitleSeq,
            }, token);
        // Swap d'enchant : étape « titles » du pipeline stats →
        // retrait de l'ancien bonus, ajout du nouveau (miroir
        // RemoveEnchantByID + EffectToLife(-2,0x3020) natifs).
        await ProfileStatEmitter.SendForCharacterAsync(session,
            _services, (int)st.CharId, token);
    }

    /// <summary>NCID 94 — requête du titre d'un perso visible.
    /// Natif : la cible est résolue par GetCharacter(node, uid) —
    /// hors node/instance, aucune réponse n'est émise.</summary>
    private async Task HandleQueryAppellationAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        var targetId = payload.Length >= 4
            ? BitConverter.ToInt32(payload, 0) : 0;
        var tst = _states.Values.FirstOrDefault(o => o.Migrated &&
            o.CharId == targetId && o.NodeId == st.NodeId &&
            o.InstanceGroup == st.InstanceGroup);
        if (tst == null) return;
        // 396 [i32 cible][i32 titre actif][u8 seq] — le natif lit
        // char+0x6fc/+0x4df de la CIBLE.
        await session.SendAsync(new CNC_CZ_ClientChangeAppellation
        {
            Field1 = targetId,
            Field2 = tst.ActiveTitle,
            Field3 = tst.TitleSeq,
        }, token);
        // 280 attr 0x2c — valeur native = char+0x47c (byte du bloc
        // player-card adjacent au nom de carte) ; le nombre de
        // titres possédés est l'approximation la plus proche.
        var titles = await _services.Characters
            .GetAppellationsAsync(targetId);
        await SendAttrAsync(session, (uint)targetId, 0x2c,
            titles.Count, 0, token);
    }
}
