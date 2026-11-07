using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Sonde <c>@wins</c> (PLAN-ACTION-3 Q10) : liste objectivement les fenêtres
/// visibles — couche, rang, rectangle, part recouverte par des fenêtres
/// dessinées au-dessus. Remplace « j'ai l'impression que ça s'ouvre derrière »
/// par un chiffre : le test échoue (<c>[wins] FAIL</c>) si une fenêtre ouverte
/// est recouverte à plus de 50 %.
/// </summary>
public static class WindowProbe
{
    public const float MaxCoveredFraction = 0.5f;

    private readonly record struct Entry(Control Win, int Layer, int Index, Rect2 Rect);

    /// <summary>Imprime l'état. Règle vérifiée (PLAN-ACTION-3 A1) : la fenêtre
    /// ouverte ou cliquée le plus récemment est la dernière dessinée et n'est
    /// recouverte par aucune autre. Renvoie le nombre de violations.</summary>
    public static int Dump()
    {
        var entries = Collect();
        GD.Print($"[wins] {entries.Count} fenêtre(s) visible(s) — ordre d'affichage croissant");
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var covered = CoveredFraction(e.Rect, entries.Skip(i + 1).Select(x => x.Rect));
            GD.Print($"[wins] {e.Win.Name} layer={e.Layer} idx={e.Index} " +
                     $"rect=({e.Rect.Position.X:0},{e.Rect.Position.Y:0},{e.Rect.Size.X:0},{e.Rect.Size.Y:0}) " +
                     $"covered={covered * 100:0}%");
        }
        var bad = 0;
        var front = HudWindows.LastFronted();
        if (front != null && entries.Count > 0)
        {
            var top = entries[^1].Win;
            var frontEntry = entries.FirstOrDefault(e => e.Win == front);
            var cover = frontEntry.Win == null ? 0f
                : CoveredFraction(frontEntry.Rect, entries.Where(e => e.Win != front && Above(e, frontEntry)).Select(e => e.Rect));
            if (top != front || cover > MaxCoveredFraction)
            {
                bad++;
                GD.Print($"[wins] FAIL : dernière fenêtre ouverte « {front.Name} » " +
                         $"n'est pas au premier plan (au-dessus : « {top.Name} », recouverte à {cover * 100:0}%)");
            }
        }
        GD.Print(bad == 0 ? $"[wins] OK frontmost={front?.Name}" : "[wins] FAIL");
        return bad;
    }

    private static bool Above(Entry a, Entry b) =>
        a.Layer != b.Layer ? a.Layer > b.Layer : a.Index > b.Index;

    private static List<Entry> Collect()
    {
        var list = new List<Entry>();
        foreach (var w in HudWindows.Registered)
        {
            if (!GodotObject.IsInstanceValid(w) || !w.IsInsideTree() || !w.IsVisibleInTree())
                continue;
            var rect = w.GetGlobalRect();
            if (rect.Size.X < 8 || rect.Size.Y < 8) continue;
            Node top = w; // enfant direct du CanvasLayer
            while (top.GetParent() is Node p && p is not CanvasLayer) top = p;
            var layer = top.GetParent() as CanvasLayer;
            list.Add(new Entry(w, layer?.Layer ?? 0, top.GetIndex(), rect));
        }
        // Ordre de dessin : couche, puis rang du conteneur, puis ordre dans l'arbre.
        list.Sort((a, b) =>
        {
            var c = a.Layer.CompareTo(b.Layer);
            if (c != 0) return c;
            c = a.Index.CompareTo(b.Index);
            return c != 0 ? c : a.Win.GetPath().ToString().CompareTo(b.Win.GetPath().ToString());
        });
        return list;
    }

    /// <summary>Part (0-1) de <paramref name="target"/> recouverte par l'union
    /// des rectangles (échantillonnage sur une grille 16×16 : suffisant pour un
    /// diagnostic, sans dépendance).</summary>
    internal static float CoveredFraction(Rect2 target, IEnumerable<Rect2> above)
    {
        var rects = above.Where(r => r.Intersects(target)).ToArray();
        if (rects.Length == 0 || target.Size.X <= 0 || target.Size.Y <= 0) return 0;
        const int n = 16;
        var hit = 0;
        for (var ix = 0; ix < n; ix++)
            for (var iy = 0; iy < n; iy++)
            {
                var p = target.Position + new Vector2(
                    (ix + 0.5f) / n * target.Size.X, (iy + 0.5f) / n * target.Size.Y);
                if (rects.Any(r => r.HasPoint(p))) hit++;
            }
        return hit / (float)(n * n);
    }
}
