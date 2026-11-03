#if GODOT
using Godot;

namespace EdenClient.Ui;

/// <summary>
/// Curseur souris natif — le client d'origine n'affiche jamais la
/// flèche de l'OS : il blitte son propre sprite
/// (<c>UI/SystemCursors.png</c>, planche 256×128 de curseurs 32×32 —
/// la première cellule est la flèche « normale » du jeu, celle vue en
/// monde et dans les menus).
///
/// <see cref="Apply"/> découpe la cellule 0 et la pose via
/// <see cref="Input.SetCustomMouseCursor(Texture2D, Input.CursorShape, Vector2)"/>
/// — point chaud en haut-gauche (la pointe de la flèche). Idempotent ;
/// sans la texture le curseur Godot par défaut est conservé.
/// </summary>
public static class NativeCursor
{
    private static bool _applied;

    /// <summary>Applique le skin de curseur natif (une fois).</summary>
    public static void Apply()
    {
        if (_applied) return;
        _applied = true;
        try
        {
            var sheet = AssetCatalog.GetTexture("UI/SystemCursors.dds")
                ?.GetImage();
            if (sheet == null)
            {
                GD.PrintErr("[cursor] UI/SystemCursors introuvable — " +
                    "curseur OS conservé");
                return;
            }
            var cell = sheet.GetRegion(new Rect2I(0, 0, 32, 32));
            var tex = ImageTexture.CreateFromImage(cell);
            Input.SetCustomMouseCursor(tex, Input.CursorShape.Arrow,
                new Vector2(2, 1));
            GD.Print("[cursor] curseur natif appliqué (SystemCursors)");
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[cursor] {e.Message}");
        }
    }
}
#endif
