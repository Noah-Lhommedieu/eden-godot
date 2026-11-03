namespace EdenClient.Combat;

/// <summary>
/// Table des motions <c>data/db/motion.ini</c> : mapping
/// <c>(modèle, kfId) → nom d'animation</c> — la résolution exacte du
/// field5 du 272 ClientMeleeAttack (le kfId natif pioche dans le KFM
/// de l'attaquant via <c>CKfm::GetAttackID</c>).
///
/// Format (vérifié sur les données Reforged, 82 075 lignes DBn) :
/// <code>
/// [KEY_INFO]  DB0=m010,201,4,death01          — modèle,kfId,frametime,nom
/// [KEY_Table] DB186=m014,1021,0.405334,hit,NULL,,attack01
///                                       — modèle,kfId,sec,phase,lien,son,nom
/// </code>
/// Le nom d'anim est toujours la <b>dernière</b> colonne ; le modèle
/// est le préfixe KFM en minuscules (« m025 » monstre, « b/g/f/k/l/c/m »
/// corps perso). Lignes non-DB et colonnes &lt;4 ignorées.
///
/// Classe PURE (aucune dépendance Godot) — testable dans NetTests.
/// La résolution du fichier est injectée via <see cref="ResolveFile"/>
/// (le jeu y branche <c>AssetCatalog.ResolveRawAsset</c>).
/// </summary>
public sealed class MotionTable
{
    private readonly Dictionary<(string Model, int KfId), string> _anims =
        new();

    /// <summary>Résolveur de chemin raw-asset (ex.
    /// <c>data/db/motion.ini</c> → chemin absolu) — injecté par le
    /// code jeu ; null = table partagée vide.</summary>
    public static Func<string, string?>? ResolveFile;

    private static MotionTable? _shared;

    /// <summary>Table partagée (motion.ini du client) — chargée
    /// paresseusement si <see cref="ResolveFile"/> est branché.</summary>
    public static MotionTable Shared
    {
        get
        {
            if (_shared != null) return _shared;
            var t = new MotionTable();
            var path = ResolveFile?.Invoke("data/db/motion.ini");
            if (path != null)
            {
                try { t.ParseLines(Vfs.EdenVfs.ReadLines(path)); }
                catch (IOException) { /* table vide */ }
            }
            _shared = t;
            return _shared;
        }
    }

    /// <summary>Nombre d'entrées (model,kfId) connues.</summary>
    public int Count => _anims.Count;

    /// <summary>Nom d'animation pour (modèle, kfId) — null si absent.
    /// <paramref name="model"/> est normalisé en minuscules.</summary>
    public string? Lookup(string? model, int kfId)
    {
        if (model == null || kfId < 0) return null;
        return _anims.TryGetValue(
            (model.ToLowerInvariant(), kfId), out var n) ? n : null;
    }

    /// <summary>Parse un texte motion.ini (lignes DBn=…).</summary>
    public static MotionTable Parse(string content)
    {
        var t = new MotionTable();
        t.ParseLines(content.Split('\n'));
        return t;
    }

    /// <summary>Charge un fichier motion.ini.</summary>
    public static MotionTable LoadFile(string path)
    {
        var t = new MotionTable();
        t.ParseLines(Vfs.EdenVfs.ReadLines(path));
        return t;
    }

    private void ParseLines(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("DB", StringComparison.Ordinal))
                continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var cols = line[(eq + 1)..].Split(',');
            // [KEY_INFO] : 4 cols (model,kfId,frametime,nom) ;
            // [KEY_Table] : 7 cols (…,phase,lien,son,nom) — le nom
            // est toujours la dernière colonne non vide.
            if (cols.Length < 4) continue;
            var name = cols[^1].Trim();
            if (name.Length == 0 ||
                name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                continue;
            var model = cols[0].Trim().ToLowerInvariant();
            if (!int.TryParse(cols[1].Trim(), out var kfId))
                continue;
            _anims[(model, kfId)] = name;
        }
    }
}
