using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Pile d'éléments au-dessus d'une entité (joueur, mob, PNJ, monture) :
/// barre de PV, valeurs PV/PM, nom, guilde, titre, marque de quête.
/// La hauteur de départ est celle du modèle réel (union des AABB des
/// meshes visibles, monture incluse), pas une constante ; les éléments
/// s'empilent du bas vers le haut selon leur visibilité et leur taille
/// (plus de trou ni de chevauchement quand un élément est masqué).
/// </summary>
public static class OverheadLayout
{
    private const string ValsName = "__vals";
    private const float MaxTop = 30f;
    public static string DbgTop = "";

    // Nœuds de l'entité qui ne font pas partie du corps.
    private static bool IsOverlay(Node3D n) =>
        n is HpBar3D or Label3D or CollisionObject3D or Decal
        || n.Name == "TargetRing" || n.Name == "Halo"
        || n.Name == "SayBubble" || n.Name == "QuestMark"
        // « _mount » = visuel de monture : l'overhead mesure le
        // cavalier hissé sur la selle, pas le sommet de la monture
        // (crinière/feu/oreilles qui dépassent la tête du joueur).
        || n.Name == "_mount";

    private static bool IsFxMesh(MeshInstance3D mi) =>
        mi.Mesh is QuadMesh or PlaneMesh or TorusMesh or PointMesh
            or RibbonTrailMesh or TubeTrailMesh;

    /// <summary>Hauteur du haut du corps dans le repère de
    /// <paramref name="owner"/> (transforms locaux composés : valable
    /// hors arbre). Repli <paramref name="fallback"/> sans mesh.</summary>
    public static float ModelTop(Node3D owner, float fallback = 1.9f)
    {
        var top = float.MinValue;
        Walk(owner, Transform3D.Identity, ref top);
        if (top <= -900f) return fallback;
        return Mathf.Min(top, MaxTop);
    }

    private static void Walk(Node3D n, Transform3D acc, ref float top)
    {
        foreach (var ch in n.GetChildren())
        {
            if (ch is not Node3D c || !c.Visible || IsOverlay(c)) continue;
            var t = acc * c.Transform;
            if (c is MeshInstance3D mi && mi.Mesh != null && !IsFxMesh(mi))
            {
                var a = mi.GetAabb();
                for (var i = 0; i < 8; i++)
                {
                    var p = t * (a.Position + new Vector3(
                        (i & 1) == 0 ? 0f : a.Size.X,
                        (i & 2) == 0 ? 0f : a.Size.Y,
                        (i & 4) == 0 ? 0f : a.Size.Z));
                    // Name plutôt que GetPath() : Walk tourne aussi sur
                    // des visuels hors arbre (SetVisual avant AddChild) —
                    // GetPath() lève « Cannot get path of node ».
                    if (p.Y > top) { top = p.Y; DbgTop = mi.Name + " " + mi.Mesh.GetType().Name; }
                }
            }
            Walk(c, t, ref top);
        }
    }

    private static float LabelHeight(Label3D l) =>
        l.FontSize * l.PixelSize * 1.15f;

    /// <summary>Empile les éléments au-dessus de <paramref name="top"/>.
    /// Renvoie le y du haut de la pile (bulle de dialogue).</summary>
    public static float Apply(Node3D owner, float top)
    {
        var y = top + 0.04f;
        void Put(Node3D? n, float h)
        {
            if (n == null || !n.Visible) return;
            var p = new Vector3(0f, y + h * 0.5f, 0f);
            if (!n.Position.IsEqualApprox(p)) n.Position = p;
            y += h + 0.03f;
        }
        Put(owner.GetNodeOrNull<HpBar3D>("HpBar3D"), 0.12f);
        foreach (var nm in new[] { ValsName, "Label", "OwnName", "__guild",
                                    "__title" })
            if (owner.GetNodeOrNull<Label3D>(nm) is { } l)
                Put(l, LabelHeight(l));
        Put(owner.GetNodeOrNull<Label3D>("QuestMark"), 0.8f);
        return y;
    }

    /// <summary>Texte des valeurs PV/PM selon le mode ; "" si rien.
    /// <paramref name="mpKnown"/> = PM connus (soi seulement).</summary>
    public static string ValuesText(HudPrefs.ValuesMode mode, bool hpKnown,
        long hp, long maxHp, bool mpKnown, long mp, long maxMp)
    {
        var hpOn = mode is HudPrefs.ValuesMode.Hp or HudPrefs.ValuesMode.Both
            && hpKnown && maxHp > 0;
        var mpOn = mode is HudPrefs.ValuesMode.Mp or HudPrefs.ValuesMode.Both
            && mpKnown && maxMp > 0;
        // Libellés (« PV »/« PM ») et « /max » pilotés par les prefs
        // ValuesLabels / ValuesShowMax (Settings ▸ Préférences).
        var hpS = hpOn ? HudPrefs.FormatValue(
            EdenClient.Data.UiTextEn.Translate("HP"), hp, maxHp) : "";
        var mpS = mpOn ? HudPrefs.FormatValue(
            EdenClient.Data.UiTextEn.Translate("MP"), mp, maxMp) : "";
        return hpOn && mpOn ? hpS + "  " + mpS : hpS + mpS;
    }

    /// <summary>Crée/maj le Label3D des valeurs ; texte vide = masqué.</summary>
    public static void SetValues(Node3D owner, string text)
    {
        var l = owner.GetNodeOrNull<Label3D>(ValsName);
        if (text.Length == 0)
        {
            if (l != null) l.Visible = false;
            return;
        }
        if (l == null)
        {
            l = new Label3D
            {
                Name = ValsName,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 26, PixelSize = 0.0065f, OutlineSize = 8,
                OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
                Modulate = new Color(1f, 1f, 1f),
                VisibilityRangeEnd = 60f,
            };
            owner.AddChild(l);
        }
        if (l.Text != text) l.Text = text;
        l.Visible = true;
    }
}
