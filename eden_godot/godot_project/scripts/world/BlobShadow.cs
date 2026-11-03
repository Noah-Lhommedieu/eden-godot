using Godot;

namespace EdenClient.World;

/// <summary>
/// Ombre « blob » cheap sous les entités — un <see cref="Decal"/> avec
/// texture radiale sombre projetée sur le terrain (suit les reliefs).
/// Usage : <c>node.AddChild(BlobShadow.Make(0.9f))</c> — le Decal doit
/// être positionné ~0,5-1 m au-dessus du sol dans le repère du parent.
/// </summary>
public static class BlobShadow
{
    private static ImageTexture? _radial;

    /// <summary>Crée le Decal d'ombre douce. <paramref name="radius"/>
    /// = demi-largeur de l'ellipse projetée.</summary>
    public static Decal Make(float radius = 0.8f)
    {
        return new Decal
        {
            Name = "BlobShadow",
            TextureAlbedo = Radial,
            Size = new Vector3(radius * 2f, 3f, radius * 2f),
            Modulate = new Godot.Color(0f, 0f, 0f, 0.55f),
            NormalFade = 0.5f,
        };
    }

    /// <summary>Texture radiale 64×64 : disque à bord doux (alpha).</summary>
    private static ImageTexture Radial => _radial ??= BuildRadial();

    private static ImageTexture BuildRadial()
    {
        const int s = 64;
        // RGBA8 : le disque doit être dans l'ALPHA — avec L8 l'alpha vaut 1
        // partout et le Decal projetait un carré plein.
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var c = (s - 1) * 0.5f;
        for (var y = 0; y < s; y++)
        for (var x = 0; x < s; x++)
        {
            var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            var v = Mathf.Clamp(1f - Mathf.SmoothStep(0.55f, 1f, d),
                0f, 1f);
            img.SetPixel(x, y, new Godot.Color(1f, 1f, 1f, v));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
