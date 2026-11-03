using Godot;

namespace EdenClient.Scene;

/// <summary>
/// Bulle de dialogue 3D au-dessus d'une entité (NCID 268
/// <c>ClientSay</c>) — spec <c>ref-shots/shot_0082_000338.png</c> :
/// rectangle sombre à liseré clair, texte blanc, face caméra. Un
/// seul quad billboard par couche (liseré / fond / texte) avec ordre
/// de rendu explicite et sans test de profondeur (coplanaires — même
/// leçon que <c>HpBar3D</c>). Auto-détruite après
/// <see cref="SceneBubbleLogic.Duration"/>. Thread principal seulement.
/// </summary>
public partial class SpeechBubble3D : Node3D
{
    private const string NodeName = "SayBubble";
    private const int FontSize = 32;
    private const float PixelSize = 0.0075f;
    private const float Pad = 0.12f, Border = 0.035f;

    private double _life;

    /// <summary>Pose une bulle sur <paramref name="owner"/> à
    /// <paramref name="height"/> m (remplace la précédente).
    /// Texte vide → rien.</summary>
    public static SpeechBubble3D? Show(Node3D owner, string text,
        float height)
    {
        var wrapped = SceneBubbleLogic.Wrap(text);
        if (wrapped.Length == 0 || !GodotObject.IsInstanceValid(owner))
            return null;
        owner.GetNodeOrNull<SpeechBubble3D>(NodeName)?.QueueFree();
        var b = new SpeechBubble3D
        {
            Name = NodeName,
            Position = new Vector3(0f, height, 0f),
            _life = SceneBubbleLogic.Duration(text),
        };
        b.Build(wrapped);
        owner.AddChild(b);
        return b;
    }

    private void Build(string wrapped)
    {
        var font = ThemeDB.FallbackFont;
        var px = font.GetMultilineStringSize(wrapped,
            HorizontalAlignment.Center, -1, FontSize);
        var w = px.X * PixelSize + Pad * 2f;
        var h = px.Y * PixelSize + Pad * 1.4f;
        AddChild(Quad("Border", new Vector2(w + Border * 2f, h + Border * 2f),
            new Color(0.88f, 0.88f, 0.9f, 0.95f), 1));
        AddChild(Quad("Fill", new Vector2(w, h),
            new Color(0.04f, 0.04f, 0.06f, 0.9f), 2));
        AddChild(new Label3D
        {
            Name = "Text",
            Text = wrapped,
            FontSize = FontSize,
            PixelSize = PixelSize,
            OutlineSize = 0,
            Modulate = Colors.White,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            Shaded = false,
        });
        // Remonte pour que le bas de la bulle repose sur la position.
        Position += new Vector3(0f, h * 0.5f, 0f);
    }

    private static MeshInstance3D Quad(string name, Vector2 size,
        Color color, int priority) => new()
    {
        Name = name,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Mesh = new QuadMesh
        {
            Size = size,
            Material = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                RenderPriority = priority,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        },
    };

    public override void _Process(double delta)
    {
        _life -= delta;
        if (_life <= 0) QueueFree();
    }
}
