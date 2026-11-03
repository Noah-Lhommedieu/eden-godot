using EdenClient.World;
using Godot;

namespace EdenClient.Native;

/// <summary>
/// DDS EDENFR → <see cref="Image"/> sans passer par PNG : les blocs BC1/2/3/4/5 sont transmis
/// tels quels au GPU (mips comprises) ; les formats 16/24/32 bits non compressés sont
/// convertis en RGBA8 (le corpus compte 14 459 R5G6B5, 1 008 A8R8G8B8…).
/// </summary>
public static class DdsTexture
{
    /// <summary>Image du .dds, ou null si l'en-tête est illisible ou le format inconnu.</summary>
    public static Image? Load(byte[] d, out string? error)
    {
        error = null;
        if (DdsInfo.TryParse(d, out var info)) return FromKnown(d, info, out error);
        return FromMasks(d, out error);
    }

    static Image? FromKnown(byte[] d, DdsInfo info, out string? error)
    {
        error = null;
        int total = 0, w = info.Width, h = info.Height;
        for (int i = 0; i < info.MipLevels; i++)
        {
            total += info.MipSize(w, h);
            w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1);
        }
        if (info.DataOffset + total > d.Length)
        {
            error = "DDS tronqué";
            return null;
        }
        var body = d.AsSpan(info.DataOffset, total).ToArray();
        bool mips = info.MipLevels > 1;
        // Chaîne de mips écourtée (ex. efs108003.dds : 8 niveaux sur 10) : Godot attend la chaîne complète,
        // on complète les derniers niveaux avec le dernier bloc (couleur moyenne de la zone).
        if (mips)
        {
            int full = 1 + (int)Math.Floor(Math.Log2(Math.Max(info.Width, info.Height)));
            if (info.MipLevels < full)
            {
                var padded = new List<byte>(body);
                int pw = Math.Max(1, info.Width >> info.MipLevels), ph = Math.Max(1, info.Height >> info.MipLevels);
                int bs = info.MipSize(4, 4) > 0 && info.Format is DdsInfo.DdsFormat.Bc1 or DdsInfo.DdsFormat.Bc2 or DdsInfo.DdsFormat.Bc3 or DdsInfo.DdsFormat.Bc4 or DdsInfo.DdsFormat.Bc5
                    ? info.MipSize(4, 4) : 0;
                for (int l = info.MipLevels; l < full; l++)
                {
                    int need = info.MipSize(pw, ph);
                    for (int k = 0; k < need; k++)
                        padded.Add(bs > 0 ? body[body.Length - bs + (k % bs)] : (byte)0);
                    pw = Math.Max(1, pw >> 1); ph = Math.Max(1, ph >> 1);
                }
                body = padded.ToArray();
            }
        }
        switch (info.Format)
        {
            case DdsInfo.DdsFormat.Bc1 when HasPunchThrough(body, info):
            {
                // Godot 4 charge Dxt1 sans alpha (BC1_RGB) : le noir des pixels transparents (feuillage, icônes,
                // chiffres de dégâts, logos) sortait en blocs noirs. On décode en RGBA8 (alpha 1 bit conservé).
                var rgba = DdsDecode.Mip0ToRgba8(d, info);
                if (rgba == null) { error = "BC1 illisible"; return null; }
                var img = Image.CreateFromData(info.Width, info.Height, false, Image.Format.Rgba8, rgba);
                return img;
            }
            case DdsInfo.DdsFormat.Bc1: return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.Dxt1, body);
            case DdsInfo.DdsFormat.Bc2: return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.Dxt3, body);
            case DdsInfo.DdsFormat.Bc3: return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.Dxt5, body);
            case DdsInfo.DdsFormat.Bc4: return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.RgtcR, body);
            case DdsInfo.DdsFormat.Bc5: return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.RgtcRg, body);
            case DdsInfo.DdsFormat.Lum8: return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.L8, body);
            case DdsInfo.DdsFormat.Rgba32: return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.Rgba8, body);
            case DdsInfo.DdsFormat.Bgra32:
                for (int i = 0; i + 3 < body.Length; i += 4) (body[i], body[i + 2]) = (body[i + 2], body[i]);
                return Image.CreateFromData(info.Width, info.Height, mips, Image.Format.Rgba8, body);
            default:
                error = "format DDS non géré";
                return null;
        }
    }

    /// <summary>Un bloc BC1 en mode 1 bit (c0 ≤ c1) dont un texel utilise l'index 3 = transparent.</summary>
    static bool HasPunchThrough(byte[] body, DdsInfo info)
    {
        int blocks = ((info.Width + 3) / 4) * ((info.Height + 3) / 4);
        for (int b = 0; b < blocks && b * 8 + 8 <= body.Length; b++)
        {
            int o = b * 8;
            if ((body[o] | body[o + 1] << 8) > (body[o + 2] | body[o + 3] << 8)) continue;
            uint idx = BitConverter.ToUInt32(body, o + 4);
            for (int t = 0; t < 16; t++)
                if (((idx >> (2 * t)) & 3) == 3) return true;
        }
        return false;
    }

    /// <summary>Formats RGB non compressés décrits par masques : 16 bits (R5G6B5, A1R5G5B5, A4R4G4B4) et 24 bits.</summary>
    static Image? FromMasks(byte[] d, out string? error)
    {
        error = null;
        if (d.Length < 0x80 || BitConverter.ToUInt32(d, 0) != 0x20534444) { error = "pas un DDS"; return null; }
        int h = BitConverter.ToInt32(d, 0x0c), w = BitConverter.ToInt32(d, 0x10);
        int mipCount = Math.Max(1, BitConverter.ToInt32(d, 0x1c));
        uint pf = BitConverter.ToUInt32(d, 0x50), bits = BitConverter.ToUInt32(d, 0x58);
        uint rm = BitConverter.ToUInt32(d, 0x5c), gm = BitConverter.ToUInt32(d, 0x60),
             bm = BitConverter.ToUInt32(d, 0x64), am = BitConverter.ToUInt32(d, 0x68);
        if ((pf & 0x40) == 0 || (bits != 16 && bits != 24) || w <= 0 || h <= 0)
        {
            error = $"DDS non géré (pf={pf:x} bits={bits})";
            return null;
        }
        int bpp = (int)bits / 8, pos = 0x80, cw = w, ch = h;
        var rgba = new List<byte>(w * h * 4 * 4 / 3);
        for (int m = 0; m < mipCount; m++)
        {
            if (pos + cw * ch * bpp > d.Length) { error = "DDS tronqué"; return null; }
            for (int i = 0; i < cw * ch; i++, pos += bpp)
            {
                uint v = bpp == 2 ? BitConverter.ToUInt16(d, pos) : (uint)(d[pos] | d[pos + 1] << 8 | d[pos + 2] << 16);
                rgba.Add(Scale(v, rm)); rgba.Add(Scale(v, gm)); rgba.Add(Scale(v, bm));
                rgba.Add(am == 0 ? (byte)255 : Scale(v, am));
            }
            cw = Math.Max(1, cw >> 1); ch = Math.Max(1, ch >> 1);
        }
        return Image.CreateFromData(w, h, mipCount > 1, Image.Format.Rgba8, rgba.ToArray());
    }

    /// <summary>Extrait le champ de bits défini par <paramref name="mask"/> et le ramène à 0..255.</summary>
    static byte Scale(uint v, uint mask)
    {
        if (mask == 0) return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint max = mask >> shift, f = (v & mask) >> shift;
        return (byte)((f * 255 + max / 2) / max);
    }
}
