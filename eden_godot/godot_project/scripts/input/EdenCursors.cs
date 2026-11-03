using Godot;

namespace EdenClient.GameInput;

/// <summary>
/// Curseur contextuel façon client EE : sprite extrait de
/// <c>assets/textures/UI/SystemCursors.png</c> (grille 8×4 de 32 px).
///
/// <b>Preuve mapping index→cellule</b> : <c>client_game
/// FUN_007b8950</c> @0x007b8950 parcourt la texture par blocs de
/// 32 px (<c>&gt;&gt;5</c>) row-major et empile chaque cellule dans
/// le vecteur des curseurs — l'index curseur N est donc la cellule
/// N (col = N%8, row = N/8).
///
/// <b>Indices observés dans le désassemblage</b> (écritures de
/// <c>*DAT_01060790</c>, le curseur courant ; 0x12=18 = cellule vide
/// = « pas de curseur custom » d'après le constructeur
/// FUN_007b8930) :
///   - 0/1 : flèche (FUN_0078ac70 — survol générique),
///   - 2   : type d'objet 0x43 (FUN_0078bf30 case 0x43),
///   - 3   : entité attaquable (FUN_0078bf30 case 7/0x13 —
///           flèche rouge, visuellement confirmé),
///   - 2   : objet interactif (case 0x43),
///   - 3   : cible attaquable (cases 7/0x13),
///   - 5-10: états contextuels de cible (case 0x33).
///
/// La planche pixel 8×4 confirme la cellule 5 comme bulle de dialogue,
/// 3 comme épée rouge et 2 comme main. Les cellules 11+ représentent
/// des outils/objets, pas les états système Busy/Forbidden : ces
/// derniers emploient donc les curseurs OS.
/// </summary>
public static class EdenCursors
{
    /// <summary>Mode de curseur affiché.</summary>
    public enum Mode
    {
        /// <summary>Flèche normale (cellule 0).</summary>
        Arrow,
        /// <summary>Flèche alternative grise (cellule 1 — survol
        /// UI non-actionnable).</summary>
        ArrowDim,
        /// <summary>Aller/interagir — flèche + main (cellule 2).</summary>
        Move,
        /// <summary>Cible attaquable — flèche rouge (cellule 3).</summary>
        Attack,
        /// <summary>Objet/loot interactif (main, cellule 2).</summary>
        Interact,
        /// <summary>Parler à un NPC — bulle (cellule 5).</summary>
        Talk,
        /// <summary>Occupé/chargement (cellule 11 — forme
        /// sablier bleue, HYPOTHESIS).</summary>
        Busy,
        /// <summary>Action impossible (cellule 12 —
        /// HYPOTHESIS).</summary>
        Forbid,
        /// <summary>Drag valide (cellule 14 — HYPOTHESIS).</summary>
        DragOk,
        /// <summary>Drag invalide (cellule 15, variante grise —
        /// HYPOTHESIS).</summary>
        DragBad,
    }

    private const int CellSize = 32;

    // index natif → cellule row-major (FUN_007b8950).
    private static int CellOf(Mode m) => m switch
    {
        Mode.ArrowDim => 1,
        Mode.Move => 2,
        Mode.Attack => 3,
        Mode.Interact => 2,
        Mode.Talk => 5,
        _ => 0,
    };

    private static bool HasNativeCell(Mode mode) =>
        mode is Mode.Arrow or Mode.ArrowDim or Mode.Move or
            Mode.Attack or Mode.Interact or Mode.Talk;

    private static ImageTexture? _sheet;
    private static bool _tried;
    private static Mode? _current;
    private static readonly Dictionary<Mode, AtlasTexture> _cache =
        new();

    /// <summary>Mode courant (diagnostic --cursor-test).</summary>
    public static Mode Current => _current ?? Mode.Arrow;

    /// <summary>Applique le curseur <paramref name="mode"/> — n'opère
    /// que sur changement (l'appel par frame est quasi gratuit ; le
    /// hover entité peut donc rester à ~15 Hz côté appelant sans
    /// reconstruire l'AtlasTexture à chaque fois).</summary>
    public static void Set(Mode mode)
    {
        if (_current == mode) return;
        _current = mode;
        var sheet = Sheet();
        if (!HasNativeCell(mode))
        {
            Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
            Input.SetDefaultCursorShape(mode switch
            {
                Mode.Busy => Input.CursorShape.Busy,
                Mode.Forbid or Mode.DragBad =>
                    Input.CursorShape.Forbidden,
                Mode.DragOk => Input.CursorShape.Drag,
                _ => Input.CursorShape.Arrow,
            });
            return;
        }
        if (sheet == null)
        {
            Input.SetDefaultCursorShape(mode switch
            {
                Mode.Talk or Mode.Interact =>
                    Input.CursorShape.PointingHand,
                Mode.Attack => Input.CursorShape.Cross,
                Mode.Busy => Input.CursorShape.Busy,
                Mode.Forbid or Mode.DragBad => Input.CursorShape.Forbidden,
                _ => Input.CursorShape.Arrow,
            });
            return;
        }
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        if (!_cache.TryGetValue(mode, out var atlas))
        {
            var idx = CellOf(mode);
            atlas = new AtlasTexture
            {
                Atlas = sheet,
                Region = new Rect2((idx % 8) * CellSize,
                    (idx / 8) * CellSize, CellSize, CellSize),
            };
            _cache[mode] = atlas;
        }
        // Hotspot : la flèche pointe haut-gauche ; les autres centrées
        // (HYPOTHESIS — le hotspot natif n'est pas lu depuis le .dds).
        var hotspot = mode is Mode.Arrow or Mode.ArrowDim
            ? Vector2.Zero
            : new Vector2(CellSize / 2f, CellSize / 2f);
        Input.SetCustomMouseCursor(atlas,
            Input.CursorShape.Arrow, hotspot);
    }

    /// <summary>Curseurs de redimensionnement fenêtre : la planche
    /// n'a pas de cellule dédiée confirmée → formes système Godot
    /// (le natif utilise d'ailleurs les images DejaVu du skin CEGUI
    /// pour le sizing, pas SystemCursors).</summary>
    public static void SetResize(Input.CursorShape shape) =>
        Input.SetDefaultCursorShape(shape);

    /// <summary>Reset du cache courant (changement de scène — force
    /// la réapplication au prochain Set).</summary>
    public static void Reset() => _current = null;

    private static ImageTexture? Sheet()
    {
        if (_tried) return _sheet;
        _tried = true;
        _sheet = AssetCatalog.GetTexture("UI/SystemCursors.png");
        if (_sheet == null)
            GD.PrintErr("[cursor] UI/SystemCursors.png introuvable — " +
                "formes OS");
        return _sheet;
    }
}
