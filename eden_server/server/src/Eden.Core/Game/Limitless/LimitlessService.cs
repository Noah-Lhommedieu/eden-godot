using Dapper;
using Npgsql;

using Eden.Core.Logging;

namespace Eden.Core.Game.Limitless;

/// <summary>Exp Ultime & Limitless Pass (chantier 3 — voir
/// docs/limitless/LIMITLESS_SYSTEM_DESIGN.md). Progression par
/// perso au niveau max : +1 Exp Ultime/kill (mob à ±5 niveaux,
/// sort lancé), paliers → perks dynamiques.
///
/// Sources des perks/récompenses (fusionnées à chaque
/// <see cref="ReloadAsync"/>) :
/// <list type="bullet">
/// <item>BDD <c>limitless_perks</c> (ids > 0, journalisées dans
/// <c>player_limitless_perks</c>) ;</item>
/// <item>fichier <c>limitless_pass.ini</c> du DATA_DIR (ids
/// synthétiques ≤ 0, jamais journalisés — pas de ligne en base à
/// référencer). Format : sections <c>[level]</c> (courbe exp,
/// surcharge <c>ll_level_cost</c>) et <c>[reward]</c>
/// (<c>niveau | code | kind | valeur | libellé</c>) — voir
/// docs/limitless/LIMITLESS_PASS_INI.md.</item>
/// </list></summary>
public sealed class LimitlessService
{
    private readonly NpgsqlDataSource _db;
    private readonly string? _passIniPath;
    private List<Perk> _dbPerks = new();
    private Dictionary<int, long> _dbLevelCost = new();
    private List<Perk> _iniPerks = new();
    private Dictionary<int, long> _iniLevelCost = new();
    /// <summary>Perks fusionnées (BDD + ini) — jamais null après le
    /// premier merge ; vide = système inerte (aucun perk accordé).</summary>
    private List<Perk> _perks = new();
    /// <summary>Courbe fusionnée (BDD + ini) — vide = pas de paliers
    /// (LevelFor → 0, NextCost → MaxValue).</summary>
    private Dictionary<int, long> _levelCost = new();
    private int _passRevision;

    public record Perk(int Id, string Code, string Name,
        string Description, int RequiredLevel, double Value,
        string ValueKind, bool Enabled);
    public record Progress(int Level, long Exp, int NextExp);

    /// <summary>Un palier du Limitless Pass pour l'UI client (NCID
    /// 2033) : niveau, exp cumulée requise et récompenses octroyées à
    /// ce palier (perks DB + ini, activées).</summary>
    public record PassTier(int Level, long ExpReq,
        IReadOnlyList<Perk> Rewards);

    /// <summary>Révision de la config du pass (perks + courbe) —
    /// incrémentée à chaque <see cref="ReloadAsync"/> ; l'émetteur
    /// NCID 2033 l'utilise pour ne renvoyer la liste des paliers
    /// qu'aux connexions qui n'ont pas encore vu cette version.</summary>
    public int PassRevision => _passRevision;

    /// <param name="passIniPath">chemin du .ini des récompenses
    /// (typiquement <c>&lt;DATA_DIR&gt;/limitless_pass.ini</c> ;
    /// null = perks BDD seules).</param>
    public LimitlessService(NpgsqlDataSource db,
        string? passIniPath = null)
    {
        _db = db;
        _passIniPath = passIniPath;
    }

    /// <summary>Recharge perks (BDD + .ini) et courbe de coût
    /// (BDD <c>ll_level_cost</c> + surcharge <c>[level]</c> du .ini)
    /// — hot-reload admin, les perks sont dynamiques par design.
    /// Tolérant : une source en échec (tables absentes, fichier
    /// illisible) conserve l'état précédent de cette source — le
    /// service reste fonctionnel sur ce qui a chargé.</summary>
    public async Task ReloadAsync()
    {
        try
        {
            await using var c = await _db.OpenConnectionAsync();
            _dbPerks = (await c.QueryAsync<Perk>(
                "SELECT id AS Id, code AS Code, name AS Name," +
                " COALESCE(description,'') AS Description," +
                " required_level AS RequiredLevel," +
                " value AS Value, value_kind AS ValueKind," +
                " enabled AS Enabled " +
                "FROM limitless_perks ORDER BY required_level")).ToList();
            _dbLevelCost = (await c.QueryAsync<(int Lv, long Req)>(
                "SELECT level AS Lv, exp_req AS Req FROM ll_level_cost"))
                .ToDictionary(x => x.Lv, x => x.Req);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Limitless] reload BDD ignoré (tables " +
                $"absentes ?) : {ex.Message}");
        }
        // Le .ini est relu à chaque appel : modifier le fichier puis
        // `//ll_reload` suffit, pas de restart. Marche aussi seul si
        // les tables BDD sont absentes (mode dégradé utile en dev).
        LoadPassIni();
        MergePass();
        _passRevision++;
        EdenLog.Write($"[Limitless] chargé : {_dbPerks.Count} perks BDD" +
            $" + {_iniPerks.Count} ini, {_levelCost.Count} paliers " +
            $"(rev {_passRevision})");
    }

    /// <summary>Fusionne BDD + ini dans <see cref="_perks"/> et
    /// <see cref="_levelCost"/> — la section [level] du .ini
    /// surcharge/complète la courbe BDD (même niveau = valeur .ini
    /// prioritaire ; un palier BDD ne peut être retiré que par UPDATE
    /// en base).</summary>
    private void MergePass()
    {
        _perks = _dbPerks.Concat(_iniPerks).ToList();
        _levelCost = new Dictionary<int, long>(_dbLevelCost);
        foreach (var (lv, req) in _iniLevelCost)
            _levelCost[lv] = req;
    }

    /// <summary>Re-parse <c>limitless_pass.ini</c> puis fusionne —
    /// public pour les tests ; appelé par <see cref="ReloadAsync"/>.
    /// Tolérant : fichier absent ou ligne invalide → ignoré et
    /// logué, jamais d'exception.</summary>
    public void LoadPassIni()
    {
        _iniPerks = new List<Perk>();
        _iniLevelCost = new Dictionary<int, long>();
        if (_passIniPath == null || !File.Exists(_passIniPath))
        {
            MergePass();
            return;
        }
        var section = "";
        var lineNo = 0;
        foreach (var raw in File.ReadAllLines(_passIniPath))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') ||
                line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            if (section == "level")
            {
                // `niveau = expCumuléeRequise` — surcharge ll_level_cost.
                var eq = line.IndexOf('=');
                if (eq <= 0 ||
                    !int.TryParse(line[..eq].Trim(), out var lv) ||
                    lv <= 0 ||
                    !long.TryParse(line[(eq + 1)..].Trim(), out var req) ||
                    req < 0)
                {
                    EdenLog.Write($"[Limitless] ini ligne {lineNo} " +
                        "ignorée ([level] attend `lv = expReq`)");
                    continue;
                }
                _iniLevelCost[lv] = req;
                continue;
            }
            if (section == "reward")
            {
                // `niveau | code | kind | valeur | libellé` — perk
                // dynamique octroyée quand le perso atteint `niveau`.
                var f = line.Split('|');
                if (f.Length < 5 ||
                    !int.TryParse(f[0].Trim(), out var lv) || lv <= 0 ||
                    !double.TryParse(f[3].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var val))
                {
                    EdenLog.Write($"[Limitless] ini ligne {lineNo} " +
                        "ignorée ([reward] attend " +
                        "`lv|code|kind|valeur|libellé`)");
                    continue;
                }
                var code = f[1].Trim();
                var kind = f[2].Trim().ToLowerInvariant();
                var name = string.Join('|', f.Skip(4)).Trim();
                if (code.Length == 0 || name.Length == 0 ||
                    kind is not ("mult" or "flat" or "percent"))
                {
                    EdenLog.Write($"[Limitless] ini ligne {lineNo} " +
                        "ignorée (code/libellé vide ou kind " +
                        "hors mult|flat|percent)");
                    continue;
                }
                // Id synthétique négatif : distingue les perks ini des
                // perks BDD (journal player_limitless_perks a une FK
                // sur limitless_perks.id — les perks ini n'y sont
                // jamais insérées).
                _iniPerks.Add(new Perk(-lineNo, code, name, name,
                    lv, val, kind, true));
                continue;
            }
        }
        MergePass();
    }

    /// <summary>Paliers du pass pour l'UI (NCID 2033) : tous les
    /// niveaux de la courbe (BDD + ini), triés, avec leurs
    /// récompenses activées (perks dont required_level == palier).
    /// Les libellés passent par <see cref="SanitizeLabel"/> :
    /// <b>ASCII pur</b> — la police du client (CEGUI F001-*) ne rend
    /// pas les séquences UTF-8 multi-octets (les libellés BDD
    /// accentués/FR sortaient en mojibake à l'écran) — la solution
    /// définitive est côté émission : décomposition FormD + retrait
    /// des marques diacritiques + rejet des caractères non-ASCII,
    /// plus les relibellés EN explicites des libellés
    /// FR/imprécis.</summary>
    public IReadOnlyList<PassTier> GetPassTiers()
    {
        var costs = _levelCost;
        if (costs.Count == 0) return Array.Empty<PassTier>();
        return costs.Keys.OrderBy(k => k)
            .Select(lv => new PassTier(lv, costs[lv],
                _perks.Where(p => p.Enabled && p.RequiredLevel == lv)
                     .Select(p => p with { Name = SanitizeLabel(p.Name) })
                     .ToList()))
            .ToList();
    }

    /// <summary>Normalise un libellé de palier pour l'affichage
    /// client (NCID 2033) : texte <b>ASCII pur</b> — la police du
    /// client (CEGUI F001-*) ne rend pas les séquences UTF-8
    /// multi-octets ; tout octet ≥ 0x80 émis sur le fil sort en
    /// mojibake à l'écran (ex. « × » U+00D7 = C3 97 → « Ã— ») ou en
    /// « ? » après rejet. Deux passes :
    /// <list type="number">
    /// <item><b>Equivalences ASCII</b> pour les symboles courants des
    /// libellés admin (« × » → « x », tirets typographiques → « - »,
    /// guillemets → « ' »/« " », « … » → « ... », exposants →
    /// chiffres) — <b>avant</b> la translittération, sinon « Drop ×2 »
    /// devenait « Drop ?2 » (régression constatée Lv5/Lv10).</item>
    /// <item>Décomposition FormD + retrait des marques diacritiques
    /// (é→e, ç→c) puis rejet → '?' de tout caractère ≥ 0x80
    /// restant.</item>
    /// </list>
    /// Puis relibellés EN explicites pour les libellés BDD
    /// historiques FR/imprécis (« Cast -10% » → « Casting time
    /// -10% », « Vitesse » → « Move speed »). Jamais vide (« - »).
    /// </summary>
    internal static string SanitizeLabel(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "-";
        var sb = new System.Text.StringBuilder(s.Length + 8);
        // Passe 1 — symboles typographiques fréquents dans les
        // libellés → équivalent ASCII lisible (avant FormD : « × »
        // n'est pas une lettre diacritée, il tomberait en '?').
        foreach (var ch in s.Trim())
        {
            switch (ch)
            {
                case '×':            sb.Append('x'); break;
                case '÷':            sb.Append('/'); break;
                case '‐': case '‑': case '–': case '—':
                case '―':            sb.Append('-'); break;
                case '‘': case '’': case '‚': case '‛':
                case '′':            sb.Append('\''); break;
                case '“': case '”': case '„': case '‟':
                case '″': case '«': case '»':
                                     sb.Append('"'); break;
                case '…':            sb.Append("..."); break;
                case '¹':            sb.Append('1'); break;
                case '²':            sb.Append('2'); break;
                case '³':            sb.Append('3'); break;
                case '°':            sb.Append(" deg"); break;
                case '€':            sb.Append("EUR"); break;
                case '•': case '·': case '∙':
                                     sb.Append("-"); break;
                default:             sb.Append(ch); break;
            }
        }
        // Passe 2 — FormD : sépare lettre+diacritique ; on drop les
        // marques (NonSpacingMark) puis tout caractère ≥ 0x80 → '?'.
        var decomposed = sb.ToString().Normalize(
            System.Text.NormalizationForm.FormD);
        sb.Clear();
        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo
                    .GetUnicodeCategory(ch) ==
                System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(ch < 0x80 ? ch : '?');
        }
        var flat = sb.ToString();
        // Relibellés EN — libellés BDD historiques (imprécis/FR).
        // NB : cast_speed_pct = vitesse d'incantation des sorts
        // (StatContribution.CastSpeedRatePercent) — « Cast -10% »
        // = temps d'incantation réduit, PAS du craft (craft_time
        // est un code séparé).
        if (flat.Contains("Cast -10%"))
            flat = flat.Replace("Cast -10%", "Casting time -10%");
        flat = flat.Replace("Vitesse", "Move speed");
        return flat;
    }

    /// <summary>Progression d'un perso — (niveau Limitless, exp
    /// ultime totale, exp pour le prochain palier).</summary>
    public async Task<Progress> GetProgressAsync(int charId)
    {
        await using var c = await _db.OpenConnectionAsync();
        var row = await c.QueryFirstOrDefaultAsync<(long Exp, int Lv)>(
            "SELECT exp AS Exp, level AS Lv FROM player_limitless " +
            "WHERE player_id=@i", new { i = charId });
        var next = NextCost(row.Lv + 1);
        return new Progress(row.Lv, row.Exp, next);
    }

    private int NextCost(int lvl) =>
        _levelCost.TryGetValue(lvl, out var r)
            ? (int)Math.Min(r, int.MaxValue) : int.MaxValue;

    /// <summary>Résultat d'un crédit d'Exp Ultime — total, niveau
    /// dérivé et indicateur de passage de niveau (pour l'annonce).</summary>
    public record GrantResult(long Exp, int Level, bool LeveledUp);

    /// <summary>Crédite `points` d'Exp Ultime — met à jour exp + le
    /// niveau dérivé (max lvl dont le coût cumulé est atteint), et
    /// journalise les perks nouvellement débloqués.</summary>
    public async Task<GrantResult> AddExpAsync(int charId, int points)
    {
        await using var c = await _db.OpenConnectionAsync();
        await c.ExecuteAsync(
            "INSERT INTO player_limitless (player_id, exp) " +
            "VALUES (@i, @p) ON CONFLICT (player_id) " +
            "DO UPDATE SET exp = player_limitless.exp + @p",
            new { i = charId, p = points });
        var exp = await c.ExecuteScalarAsync<long>(
            "SELECT exp FROM player_limitless WHERE player_id=@i",
            new { i = charId });
        var lvl = LevelFor(exp);
        await c.ExecuteAsync(
            "UPDATE player_limitless SET level=@l WHERE player_id=@i",
            new { l = lvl, i = charId });
        foreach (var pk in _perks.Where(
            x => x.Enabled && x.RequiredLevel <= lvl && x.Id > 0))
            // Id > 0 : seules les perks BDD sont journalisées (FK
            // sur limitless_perks.id — les perks ini portent un
            // id synthétique négatif sans ligne en base).
            await c.ExecuteAsync(
                "INSERT INTO player_limitless_perks " +
                "(player_id, perk_id) VALUES (@i,@k) " +
                "ON CONFLICT DO NOTHING",
                new { i = charId, k = pk.Id });
        return new GrantResult(exp, lvl,
            lvl > LevelFor(exp - points));
    }

    private int LevelFor(long exp)
    {
        var lvl = 0;
        foreach (var (l, req) in _levelCost.OrderBy(x => x.Key))
            if (exp >= req) lvl = l; else break;
        return lvl;
    }

    /// <summary>Perks actifs d'un perso (enabled + required <=
    /// level) — les effets se recalculent, le journal garde
    /// l'historique.</summary>
    public async Task<IReadOnlyList<Perk>> GetActivePerksAsync(
        int charId)
    {
        var p = await GetProgressAsync(charId);
        return _perks.Where(x => x.Enabled &&
            x.RequiredLevel <= p.Level).ToList();
    }

    /// <summary>Valeur agrégée d'un perk par code (mult = produit,
    /// flat/percent = somme) pour un perso.</summary>
    public async Task<double> GetPerkValueAsync(int charId,
        string code)
    {
        var perks = await GetActivePerksAsync(charId);
        var sel = perks.Where(x => x.Code == code).ToList();
        if (sel.Count == 0) return 0;
        return sel.Any(x => x.ValueKind == "mult")
            ? sel.Where(x => x.ValueKind == "mult")
                 .Aggregate(1.0, (a, x) => a * x.Value)
            : sel.Sum(x => x.Value);
    }
}
