using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Limitless.Studio.Services;

/// <summary>Une entité du jeu qui porte un code d'icône —
/// item (S_Item col1) ou monstre (S_Monster col1 = code modèle,
/// l'icône uiicon est résolue par strip des chiffres de fin).</summary>
public sealed record IconEntity(
    string Kind,      // "item" | "monster"
    int Id,
    string Name,      // EN via T_*, sinon "[untranslated] <big5>"
    string IconCode);

/// <summary>Index entité ↔ icône : construit une fois à partir de
/// S_Item/S_Monster + libellés EN (t_item/t_monster). Sert à :
/// 1) chercher l'icône d'un item/mob par son NOM (« box »,
/// « bonbon bleu ») ; 2) inversement, lister les entités qui
/// utilisent un fichier icône donné (UsedBy) via les variantes
/// digit-strip d'IconService.</summary>
public sealed class EntityIconIndex
{
    private readonly StudioContext _ctx;
    private List<IconEntity>? _all;
    private Dictionary<string, List<IconEntity>>? _byVariant;

    public EntityIconIndex(StudioContext ctx) => _ctx = ctx;

    public bool Loaded => _all != null;
    public int Count => _all?.Count ?? 0;

    /// <summary>Construit l'index (coûteux — appeler en tâche de
    /// fond). Idempotent.</summary>
    public void EnsureLoaded()
    {
        if (_all != null) return;
        var all = new List<IconEntity>(80_000);
        var byVar = new Dictionary<string, List<IconEntity>>(
            StringComparer.OrdinalIgnoreCase);

        void Add(string kind, int id, string iconCode, string big5,
            string? en)
        {
            if (iconCode.Length == 0) return;
            var name = en ?? (big5.Length > 0
                ? $"[untranslated] {big5}" : "(no name)");
            var e = new IconEntity(kind, id, name, iconCode);
            all.Add(e);
            foreach (var v in IconService.CodeVariants(iconCode))
            {
                if (!byVar.TryGetValue(v, out var list))
                    byVar[v] = list = new();
                list.Add(e);
            }
        }

        // Records (pas Rows) : les boss S_Monster et les items à
        // description multi-lignes couvrent plusieurs lignes
        // physiques — une ligne de continuation qui commence par un
        // chiffre fabriquerait un faux id.
        var items = Path.Combine(_ctx.IniDir, "S_Item.ini");
        foreach (var rec in _ctx.Ini.Records(items))
        {
            var c = rec.Cols;
            if (c.Length < 2 || !int.TryParse(c[0], out var id))
                continue;
            Add("item", id, c[1], c.Length > 7 ? c[7] : "",
                _ctx.Trans.ItemName(id));
        }
        var mobs = Path.Combine(_ctx.IniDir, "S_Monster.ini");
        foreach (var rec in _ctx.Ini.Records(mobs))
        {
            var c = rec.Cols;
            if (c.Length < 3 || !int.TryParse(c[0], out var id))
                continue;
            Add("monster", id, c[1], ShortName(c[2]),
                _ctx.Trans.MonsterName(id));
        }
        _byVariant = byVar;
        _all = all;
    }

    /// <summary>Nom « court » d'une colonne nom — les records boss à
    /// 2 lignes portent « titre\nnom » en col2 : on garde la dernière
    /// ligne (le nom) pour l'affichage/recherche.</summary>
    internal static string ShortName(string col)
    {
        var nl = col.LastIndexOf('\n');
        return nl >= 0 ? col[(nl + 1)..].Trim() : col;
    }

    /// <summary>Entités dont le nom EN/Big5 contient `q` ou dont
    /// l'id/code correspond exactement.</summary>
    public IReadOnlyList<IconEntity> Search(string q, int take = 60)
    {
        if (_all == null || string.IsNullOrWhiteSpace(q))
            return Array.Empty<IconEntity>();
        var hits = _all.Where(e =>
                e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Id.ToString() == q
                || e.IconCode.Equals(q,
                    StringComparison.OrdinalIgnoreCase))
            .Take(take).ToList();
        return hits;
    }

    /// <summary>Entités dont le code icône (ou une variante
    /// digit-strip) correspond à `fileBase` — le nom de fichier sans
    /// extension d'une tuile de la grille.</summary>
    public IReadOnlyList<IconEntity> UsedBy(string fileBase,
        int take, out int total)
    {
        total = 0;
        if (_byVariant == null
            || !_byVariant.TryGetValue(fileBase, out var list))
            return Array.Empty<IconEntity>();
        total = list.Count;
        return list.Take(take).ToList();
    }
}
