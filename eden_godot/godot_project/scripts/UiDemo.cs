using Godot;
using EdenClient.Ui;

namespace EdenClient;

/// <summary>
/// Scène de démonstration du portage CEGUI → Control
/// (<c>scenes/ui_demo.tscn</c>). Au démarrage : charge le scheme, le
/// looknfeel, tous les imagesets et les fonts depuis
/// <c>raw_assets/uidata</c>, puis construit quelques layouts
/// représentatifs et les empile dans le CanvasLayer <c>UILayer</c>.
///
/// Point d'entrée volontairement hors <c>main.tscn</c> : lancer la
/// démo = ouvrir <c>scenes/ui_demo.tscn</c> (F6) ou la passer en
/// scène principale. Aucune modification du flux normal du client.
/// </summary>
public partial class UiDemo : Control
{
    /// <summary>Layouts affichés (échantillon représentatif : login,
    /// chat, fenêtre de jeu, tooltip d'objet, sac).</summary>
    private static readonly string[] DemoLayouts =
    {
        "Login.layout",
        "ChannelWnd.layout",
        "Achievement.layout",
        "ItemDeTail.layout",
        "BagManagement2.layout",
    };

    public override void _Ready()
    {
        var uidata = DataPaths.FindSubdir("uidata");
        if (uidata == null)
        {
            GD.PushError("[UiDemo] uidata introuvable (racines : " +
                string.Join(", ", DataPaths.CandidateDataRoots()) + ")");
            return;
        }

        var ctx = CeguiContext.Load(uidata);
        var textures = new UiTextureCache(ctx.Imagesets);
        var builder = new CeguiBuilder(ctx);
        var layer = GetNode<CanvasLayer>("UILayer");

        int i = 0;
        foreach (var file in DemoLayouts)
        {
            var path = Path.Combine(uidata, "layouts", file);
            var spec = builder.BuildLayoutFile(path);
            if (spec == null)
                continue;
            var ctrl = CeguiToGodot.BuildTree(spec, textures);
            layer.AddChild(ctrl);
            // Cascade pour distinguer les fenêtres à l'écran.
            ctrl.Position += new Vector2(50 * i, 35 * i);
            i++;
            GD.Print($"[UiDemo] {file} → {ctrl.Name} ({spec.Kind})");
        }

        var rep = builder.Report;
        GD.Print($"[UiDemo] layouts ok={rep.LayoutsOk} ko={rep.LayoutsFailed}");
        foreach (var (t, n) in rep.UnknownTypes)
            GD.Print($"[UiDemo] type inconnu: {t} ×{n}");
    }
}
