using System.Globalization;

namespace Eden.Zone;

/// <summary>
/// Règles PURES des scènes de quête (plan futur-feature/02) : comparaisons
/// de conditions <c>Event_If_*</c>, durée de verrou d'IA de
/// <c>Event_MonsterAction</c>, décodage des commandes texte
/// mission→zone (<c>CNE_MW_WorldTextCommand</c> NCID 20). Aucune
/// dépendance réseau/DB : testable sans serveur live.
///
/// Preuves reverse :
/// <list type="bullet">
/// <item><c>Event_If_ScreenMessage</c> mission @0x080db5f0 : le corps
///   écrit simplement <c>*result = 1</c> — la condition est TOUJOURS
///   vraie (les « 4 id 0 » des scripts sont des paramètres ignorés).</item>
/// <item><c>Event_If_BuffCheck</c> @0x080dd810 : args (buffId, mode) ;
///   mode 11 (0xb) = « le joueur porte l'enchant », 12 (0xc) = « ne le
///   porte pas ».</item>
/// <item><c>Event_If_BuffCheckByStack</c> (verbe de script
///   <c>Event_If_BuffCheck6P</c>) @0x080dd2a0 : args (buffId, portée,
///   op, valeur) ; portée 13 (0xd) = exécutant, 14 (0xe) = tous les
///   membres de l'équipe présents sur le même node ; compare
///   <c>stack</c> (short @enchant+0x20, 0 si absent) à valeur avec la
///   table op 1=≥ 2=≤ 3== 4=≠.</item>
/// <item><c>CAI::SetEventAction</c> zone @0x084d5120 : le « Time » n'est
///   PAS un scheduler d'événement chaîné — c'est la durée pendant
///   laquelle l'IA du monstre est figée (CAI+0xec = fin de verrou ; la
///   durée de la motion <c>CKfm::GetMotionEndTime</c> l'emporte quand
///   elle est connue) ; <c>Trigger_Id</c> = id du joueur déclencheur
///   (BecomeFightingCandicate).</item>
/// </list>
/// </summary>
public static class SceneEventRules
{
    /// <summary>Portée « exécutant seul » de BuffCheck6P.</summary>
    public const int ScopeSelf = 13;
    /// <summary>Portée « toute l'équipe locale » de BuffCheck6P.</summary>
    public const int ScopeTeam = 14;
    /// <summary>BuffCheck mode « présent ».</summary>
    public const int BuffPresent = 11;
    /// <summary>BuffCheck mode « absent ».</summary>
    public const int BuffAbsent = 12;
    /// <summary>Plafond de sécurité du verrou d'IA (s) — les données
    /// réelles ne dépassent pas 5 s.</summary>
    public const double MaxActionLockSeconds = 60;

    /// <summary>Comparaison native des conditions : op 1=≥, 2=≤, 3==,
    /// 4=≠ (CSceneCondLevel::Execute @0x80c33b0).</summary>
    public static bool Compare(int lhs, int op, int rhs) => op switch
    {
        1 => lhs >= rhs,
        2 => lhs <= rhs,
        3 => lhs == rhs,
        4 => lhs != rhs,
        _ => false,
    };

    /// <summary><c>Event_If_ScreenMessage</c> — toujours vrai
    /// (corps natif : <c>*result = 1</c>).</summary>
    public static bool ScreenMessageCond() => true;

    /// <summary><c>Event_If_BuffCheck</c> : mode 11 → vrai si l'enchant
    /// est porté, mode 12 → vrai s'il ne l'est pas, autre mode → faux
    /// (le natif retourne sans positionner le résultat).</summary>
    public static bool BuffPresence(bool has, int mode) => mode switch
    {
        BuffPresent => has,
        BuffAbsent => !has,
        _ => false,
    };

    /// <summary><c>Event_If_BuffCheck6P</c> pour UN personnage :
    /// compare le nombre de piles (0 si l'enchant est absent) à
    /// <paramref name="value"/> avec l'op natif.</summary>
    public static bool BuffStack(int stack, int op, int value)
        => Compare(Math.Max(0, stack), op, value);

    /// <summary>Durée (s) du verrou d'IA posé par
    /// <c>Event_MonsterAction</c>. Natif : si la motion est connue
    /// (<paramref name="motionEndDs"/> &gt; 0, décisecondes) c'est sa
    /// durée qui compte, sinon le paramètre <c>Time</c>. Bornée à
    /// [0, <see cref="MaxActionLockSeconds"/>].</summary>
    public static double ActionLockSeconds(int timeParam, int motionEndDs)
    {
        var s = motionEndDs > 0 ? motionEndDs / 10.0 : timeParam;
        return Math.Clamp(s, 0, MaxActionLockSeconds);
    }

    /// <summary>Commande texte mission→zone décodée : verbe + paires
    /// <c>Clé=</c> / valeur (le natif les écrit « Clé= %d » séparés par
    /// des espaces).</summary>
    public sealed record TextCommand(string Verb,
        IReadOnlyDictionary<string, string> Args)
    {
        /// <summary>Entier de l'argument (0 si absent/illisible).</summary>
        public int Int(string key) =>
            Args.TryGetValue(key, out var v) &&
            int.TryParse(v, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var n) ? n : 0;

        /// <summary>Flottant de l'argument (0 si absent/illisible).</summary>
        public float Float(string key) =>
            Args.TryGetValue(key, out var v) &&
            float.TryParse(v, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var n) ? n : 0f;

        /// <summary>Texte brut de l'argument ("" si absent).</summary>
        public string Str(string key) =>
            Args.TryGetValue(key, out var v) ? v : "";
    }

    /// <summary>Décode « Verbe Clé1= v1 Clé2= v2 … » (formats natifs
    /// de <c>BornNpcByTID</c>, <c>KillNpcByTID</c>,
    /// <c>MonsterActionByHandle</c>, <c>PlaySound</c> — chaînes
    /// zone @0x0887de18..0x0887df0c). Retourne null si le texte est
    /// vide. Un jeton « Clé=valeur » collé est aussi accepté.</summary>
    public static TextCommand? ParseTextCommand(string text)
    {
        var t = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0) return null;
        var args = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < t.Length; i++)
        {
            var tok = t[i];
            var eq = tok.IndexOf('=');
            if (eq <= 0) continue;
            var key = tok[..eq];
            if (eq < tok.Length - 1)
                args[key] = tok[(eq + 1)..];
            else if (i + 1 < t.Length && !t[i + 1].EndsWith('='))
                args[key] = t[++i];
        }
        return new TextCommand(t[0], args);
    }

    /// <summary>Marqueur du blob d'un 268 <c>ClientSay</c> de monstre :
    /// « DLG1 » + i32 id de dialogue. Extension Godot-first (le blob
    /// natif d'un say = item-link, vide pour un MonsterSay) : le client
    /// résout l'id dans SA langue (<c>t_dialogue</c> FR/EN) au lieu de
    /// n'afficher que le texte serveur figé.</summary>
    public static readonly byte[] DialogueBlobMagic = "DLG1"u8.ToArray();

    /// <summary>Blob « DLG1 »+id ; vide si l'id est ≤ 0.</summary>
    public static byte[] EncodeDialogueBlob(int dialogueId)
    {
        if (dialogueId <= 0) return Array.Empty<byte>();
        var b = new byte[8];
        DialogueBlobMagic.CopyTo(b, 0);
        BitConverter.GetBytes(dialogueId).CopyTo(b, 4);
        return b;
    }

    /// <summary>Numéro de fichier d'effet depuis « s2341 » (la mission
    /// écrit <c>sprintf("s%d")</c>, TC_Event_PlayEffect @0x080ecd20) ;
    /// 0 si illisible.</summary>
    public static int EffectFileNo(string fileName)
    {
        var t = fileName.StartsWith('s') ? fileName[1..] : fileName;
        return int.TryParse(t, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>Distance max (unités) d'un pas de marche scriptée —
    /// au-delà, la destination est ramenée sur ce rayon (garde contre
    /// une coordonnée aberrante envoyée par un script).</summary>
    public const float MaxScriptedWalk = 400f;

    /// <summary>Destination bornée d'une marche scriptée de
    /// (x,y) vers (dx,dy).</summary>
    public static (float X, float Y) ClampWalk(float x, float y,
        float dx, float dy)
    {
        var vx = dx - x; var vy = dy - y;
        var len = MathF.Sqrt(vx * vx + vy * vy);
        if (len <= MaxScriptedWalk || len < 0.0001f) return (dx, dy);
        var k = MaxScriptedWalk / len;
        return (x + vx * k, y + vy * k);
    }
}
