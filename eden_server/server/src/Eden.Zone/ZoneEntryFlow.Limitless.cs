using System.Buffers.Binary;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Partial <see cref="ZoneEntryFlow"/> — Exp Ultime &
/// Limitless Pass (chantier 3 — docs/limitless/LIMITLESS_SYSTEM_DESIGN.md).
/// NCIDs custom (hors plage native ≤~900, réservé Reforged/Limitless) :
/// <list type="bullet">
/// <item><b>2032</b> — état du perso : barre d'Exp Ultime + HUD.</item>
/// <item><b>2033</b> — liste des paliers du Limitless Pass (niveau,
/// exp requise, récompenses) pour la fenêtre du pass (Ctrl+L).</item>
/// </list>
/// Le client natif ignore ces NCIDs (index hors table de dispatch) ;
/// seul le mod Limitless.dll les rend.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>État Limitless d'un perso → NCID 2032 (+ liste des
    /// paliers 2033 si cette révision de config n'a pas encore été
    /// poussée sur cette connexion) au client. Tolérant : si les
    /// tables absentes, émet level/exp 0.</summary>
    private async Task SendLimitlessAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        await LimitlessEmitter.EmitAsync(session, _services.Limitless,
            (int)st.CharId, token);
        await LimitlessEmitter.EmitPassAsync(session,
            _services.Limitless, (int)st.CharId, token);
    }
}

/// <summary>Émetteur des NCIDs custom Limitless (<b>2032</b> état +
/// <b>2033</b> liste des paliers) — partagé entre le flux d'entrée
/// (<see cref="ZoneEntryFlow.SendLimitlessAsync"/>, MigrationDone +
/// gains de combat) et la commande GM <c>//ll_exp</c> qui doit
/// rafraîchir le HUD client après un grant hors combat.</summary>
internal static class LimitlessEmitter
{
    /// <summary>Dernière révision de la liste des paliers (NCID 2033)
    /// envoyée sur chaque <b>session</b> — la liste ne dépend que de la
    /// config (perks + courbe), pas de l'état du perso : on ne la
    /// renvoie qu'à la première émission d'une connexion ou après un
    /// <see cref="Eden.Core.Game.Limitless.LimitlessService.PassRevision"/>
    /// bump (reload admin). Clé = session (pas charId) : un relog
    /// recrée l'état mod côté client — il faut lui re-pousser la
    /// liste. <see cref="ConditionalWeakTable{TKey,TValue}"/> : les
    /// entrées suivent le GC des sessions, pas de fuite.</summary>
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<NetSession,
            SessionPassState> _passSent = new();

    private sealed class SessionPassState { public int Revision = -1; }

    /// <summary>Lit l'état Limitless du perso et émet
    /// <c>[i32 charId][i64 exp][s16 level][u16 perkCount][i32 nextExp]</c>
    /// sur la session. Tolérant : service nul ou tables absentes →
    /// zéros (et nextExp = 1 pour éviter une division par zéro côté
    /// client).</summary>
    public static async Task EmitAsync(NetSession session,
        Eden.Core.Game.Limitless.LimitlessService? svc, int charId,
        CancellationToken token)
    {
        int level = 0; long exp = 0; int perks = 0;
        long nextExp = int.MaxValue;
        if (svc != null)
        {
            try
            {
                var p = await svc.GetProgressAsync(charId);
                level = p.Level; exp = p.Exp;
                perks = (await svc.GetActivePerksAsync(charId)).Count;
                nextExp = p.NextExp;
            }
            catch { /* tables absentes → zéros */ }
        }
        await EmitAsync(session, charId, level, exp, perks, nextExp,
            token);
    }

    /// <summary>Surcharge à valeurs explicites — état lu hors-boucle
    /// par le pipeline d'entrée (Phase 2), zéro requête. Même payload
    /// 20B que la variante service.</summary>
    public static async Task EmitAsync(NetSession session, int charId,
        int level, long exp, int perks, long nextExp,
        CancellationToken token)
    {
        var body = new byte[20];
        // i32 charId — identifie le perso côté HUD client.
        BinaryPrimitives.WriteInt32LittleEndian(body, charId);
        // i64 exp — Exp Ultime totale (peut dépasser int.MaxValue).
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(4), exp);
        // s16 level — palier Ultimate courant.
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(12),
            (short)level);
        // u16 perkCount — nombre de perks Limitless actifs.
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(14),
            (ushort)perks);
        // i32 nextExp — seuil du palier suivant (clampé ≥1 : le client
        // calcule exp/nextExp pour la ProgressBar CEGUI).
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(16),
            (int)Math.Clamp(nextExp, 1, int.MaxValue));
        await session.SendPacketAsync(2032, body, token);
    }

    /// <summary>Émet la liste des paliers du Limitless Pass → NCID
    /// <b>2033</b>, sauf si cette révision de config a déjà été
    /// poussée sur cette session (la liste est statique entre deux
    /// <c>ReloadAsync</c> — pas de re-send par kill, mais re-push à
    /// chaque nouvelle connexion car l'état du mod est volatil).</summary>
    /// Payload : <c>[i32 charId][u16 tierCount]</c> puis pour chaque
    /// palier <c>[s16 level][i64 expReq][u16 textLen][texte UTF-8]</c>
    /// — le texte est le résumé des récompenses du palier formaté
    /// côté serveur (le client ne fait que l'afficher ; les statuts
    /// DONE/NEXT sont calculés côté mod via le level du NCID 2032).</summary>
    public static async Task EmitPassAsync(NetSession session,
        Eden.Core.Game.Limitless.LimitlessService? svc, int charId,
        CancellationToken token)
    {
        var revision = 0;
        IReadOnlyList<Eden.Core.Game.Limitless.LimitlessService.PassTier>
            tiers = Array.Empty<Eden.Core.Game.Limitless.LimitlessService
                .PassTier>();
        if (svc != null)
        {
            try
            {
                tiers = svc.GetPassTiers();
                revision = svc.PassRevision;
            }
            catch { /* service/tables absents → liste vide */ }
        }
        var st = _passSent.GetOrCreateValue(session);
        if (st.Revision == revision)
            return; // cette version de la liste déjà poussée sur cette session

        // Cap partagé avec le client (MAX_TIERS 256) — au-delà le mod
        // rejette le paquet ; une courbe > 256 paliers est aberrante.
        if (tiers.Count > 256)
            tiers = tiers.Take(256).ToList();

        // Pré-formatage des textes de paliers pour dimensionner le body.
        var texts = new List<byte[]>(tiers.Count);
        var bodyLen = 6;
        foreach (var t in tiers)
        {
            // Récompenses du palier = libellés (Name) joints ; « — »
            // si le palier n'accorde rien (trous de la courbe).
            var label = t.Rewards.Count == 0
                ? "-"
                : string.Join("; ", t.Rewards.Select(r => r.Name));
            var utf8 = TruncateUtf8(label, 200);
            texts.Add(utf8);
            bodyLen += 12 + utf8.Length;
        }
        var body = new byte[bodyLen];
        // i32 charId — identifie le perso côté client mod.
        BinaryPrimitives.WriteInt32LittleEndian(body, charId);
        // u16 tierCount — nombre de paliers sérialisés.
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4),
            (ushort)tiers.Count);
        var off = 6;
        for (var i = 0; i < tiers.Count; i++)
        {
            var t = tiers[i];
            // s16 level — palier Limitless.
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(off),
                (short)t.Level);
            // i64 expReq — Exp Ultime cumulée requise pour ce palier.
            BinaryPrimitives.WriteInt64LittleEndian(
                body.AsSpan(off + 2), t.ExpReq);
            // u16 textLen + texte UTF-8 — récap des récompenses.
            var utf8 = texts[i];
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(off + 10), (ushort)utf8.Length);
            utf8.CopyTo(body.AsSpan(off + 12));
            off += 12 + utf8.Length;
        }
        await session.SendPacketAsync(2033, body, token);
        st.Revision = revision;
    }

    /// <summary>Tronque une chaîne à <paramref name="maxBytes"/>
    /// octets UTF-8 sans couper une séquence multi-octets.</summary>
    private static byte[] TruncateUtf8(string s, int maxBytes)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes) return bytes;
        var len = maxBytes;
        // Recule tant que l'octet courant est un octet de continuation
        // UTF-8 (10xxxxxx) pour ne pas tronquer un caractère.
        while (len > 0 && (bytes[len] & 0xC0) == 0x80) len--;
        return bytes[..len];
    }
}
