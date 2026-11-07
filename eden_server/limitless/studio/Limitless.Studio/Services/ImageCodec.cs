using System;
using Avalonia;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Limitless.Studio.Services;

/// <summary>Dispatcher de formats d'image des assets .pkg —
/// route par magic bytes : PNG → Bitmap natif Avalonia, DDS →
/// <see cref="DdsDecoder"/>, TGA (non compressé, type 2/3) →
/// décodeur BGR(A) minimal. Retourne null si format inconnu —
/// le .pkg brut reste alors exportable pour un viewer externe.</summary>
public static class ImageCodec
{
    /// <summary>Détecte le format et décode en bitmap.</summary>
    public static Bitmap? Decode(byte[] d)
    {
        if (d.Length < 4) return null;
        // PNG — 89 50 4E 47.
        if (d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G')
            return new Bitmap(new MemoryStream(d));
        // DDS — «DDS ».
        if (d[0] == 'D' && d[1] == 'D' && d[2] == 'S' && d[3] == ' ')
            return DdsDecoder.Decode(d);
        // TGA — header 18 o, image_type 2 (vraies couleurs non
        // compressées) ou 3 (N&B), pas de mappe (colormaptype=0).
        if (d.Length > 18 && d[1] == 0 && (d[2] == 2 || d[2] == 3))
            return DecodeTga(d);
        return null;
    }

    /// <summary>Décode un TGA non compressé en bitmap —
    /// header 18 o `[idlen][cmaptype][type][cm×5][x][y][w][h][bpp]
    /// [desc]`, pixels BGR(A) bottom-up sauf bit5 du descripteur.</summary>
    private static Bitmap? DecodeTga(byte[] d)
    {
        var idLen = d[0];
        var w = d[12] | d[13] << 8;
        var h = d[14] | d[15] << 8;
        var bpp = d[16] / 8;
        var topDown = (d[17] & 0x20) != 0;
        if (w <= 0 || h <= 0 || bpp < 3) return null;
        var src = d.AsSpan(18 + idLen);
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var dy = topDown ? y : h - 1 - y;
            for (var x = 0; x < w; x++)
            {
                var si = (y * w + x) * bpp;
                var di = (dy * w + x) * 4;
                if (si + bpp > src.Length) return null;
                px[di] = src[si]; px[di + 1] = src[si + 1];
                px[di + 2] = src[si + 2];
                px[di + 3] = bpp == 4 ? src[si + 3] : (byte)255;
            }
        }
        var bm = new WriteableBitmap(
            new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var l = bm.Lock())
            System.Runtime.InteropServices.Marshal.Copy(
                px, 0, l.Address, px.Length);
        return bm;
    }
}
