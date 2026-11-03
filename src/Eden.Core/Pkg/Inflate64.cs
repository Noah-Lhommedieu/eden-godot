namespace EdenClient.Pkg;

/// <summary>
/// Inflate à fenêtre 64 Ko du client EDENFR : deflate standard plus les codes de
/// distance 30 et 31. <c>DeflateStream</c> (fenêtre 32 Ko) ne les accepte pas.
/// Port de tools/edenfr-verify/inflate64.c (algorithme de puff.c).
/// </summary>
public static class Inflate64
{
    static readonly short[] LBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
    static readonly short[] LExt = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
    static readonly int[] DBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577, 32769, 49153 };
    static readonly short[] DExt = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14 };
    static readonly short[] Order = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

    sealed class Huff
    {
        public readonly short[] Count = new short[16];
        public readonly short[] Symbol = new short[288];

        public int Build(short[] length, int off, int n)
        {
            Array.Clear(Count);
            for (int i = 0; i < n; i++) Count[length[off + i]]++;
            if (Count[0] == n) return 0;
            int left = 1;
            for (int l = 1; l <= 15; l++) { left <<= 1; left -= Count[l]; if (left < 0) return left; }
            var offs = new short[16];
            for (int l = 1; l < 15; l++) offs[l + 1] = (short)(offs[l] + Count[l]);
            for (int i = 0; i < n; i++) if (length[off + i] != 0) Symbol[offs[length[off + i]]++] = (short)i;
            return left;
        }
    }

    ref struct State
    {
        public ReadOnlySpan<byte> In; public int InPos; public uint Buf; public int Cnt;
        public Span<byte> Out; public int OutPos;

        public int Bits(int need)
        {
            uint v = Buf;
            while (Cnt < need)
            {
                if (InPos >= In.Length) throw new InvalidDataException("inflate64 : flux tronqué");
                v |= (uint)In[InPos++] << Cnt; Cnt += 8;
            }
            Buf = v >> need; Cnt -= need;
            return (int)(v & ((1u << need) - 1));
        }

        public int Decode(Huff h)
        {
            int code = 0, first = 0, index = 0;
            for (int len = 1; len <= 15; len++)
            {
                code |= Bits(1);
                int count = h.Count[len];
                if (code - count < first) return h.Symbol[index + (code - first)];
                index += count; first += count; first <<= 1; code <<= 1;
            }
            throw new InvalidDataException("inflate64 : code invalide");
        }
    }

    static Huff? s_fl, s_fd;

    /// <summary>Décompresse un flux deflate brut (sans en-tête zlib de 2 octets) de taille connue.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> raw, int size)
    {
        var output = new byte[size];
        var s = new State { In = raw, Out = output };
        bool last;
        do
        {
            last = s.Bits(1) == 1;
            switch (s.Bits(2))
            {
                case 0: Stored(ref s); break;
                case 1: Fixed(ref s); break;
                case 2: Dynamic(ref s); break;
                default: throw new InvalidDataException("inflate64 : type de bloc 3");
            }
        } while (!last);
        if (s.OutPos != size) throw new InvalidDataException($"inflate64 : {s.OutPos}/{size} octets");
        return output;
    }

    static void Stored(ref State s)
    {
        s.Buf = 0; s.Cnt = 0;
        if (s.InPos + 4 > s.In.Length) throw new InvalidDataException("inflate64 : flux tronqué");
        int len = s.In[s.InPos] | (s.In[s.InPos + 1] << 8);
        int nlen = s.In[s.InPos + 2] | (s.In[s.InPos + 3] << 8);
        s.InPos += 4;
        if (len != (~nlen & 0xffff)) throw new InvalidDataException("inflate64 : bloc stocké invalide");
        if (s.InPos + len > s.In.Length || s.OutPos + len > s.Out.Length) throw new InvalidDataException("inflate64 : débordement");
        s.In.Slice(s.InPos, len).CopyTo(s.Out[s.OutPos..]);
        s.InPos += len; s.OutPos += len;
    }

    static void Fixed(ref State s)
    {
        if (s_fl == null)
        {
            var l = new short[288]; int i;
            for (i = 0; i < 144; i++) l[i] = 8;
            for (; i < 256; i++) l[i] = 9;
            for (; i < 280; i++) l[i] = 7;
            for (; i < 288; i++) l[i] = 8;
            var fl = new Huff(); fl.Build(l, 0, 288);
            for (i = 0; i < 32; i++) l[i] = 5;
            var fd = new Huff(); fd.Build(l, 0, 32);
            s_fd = fd; s_fl = fl;
        }
        Codes(ref s, s_fl, s_fd!);
    }

    static void Dynamic(ref State s)
    {
        int nlen = s.Bits(5) + 257, ndist = s.Bits(5) + 1, ncode = s.Bits(4) + 4;
        if (nlen > 288 || ndist > 32) throw new InvalidDataException("inflate64 : comptes invalides");
        var lengths = new short[320];
        for (int i = 0; i < ncode; i++) lengths[Order[i]] = (short)s.Bits(3);
        var lc = new Huff();
        if (lc.Build(lengths, 0, 19) != 0) throw new InvalidDataException("inflate64 : code de longueurs incomplet");
        int n = 0;
        while (n < nlen + ndist)
        {
            int sym = s.Decode(lc);
            if (sym < 16) { lengths[n++] = (short)sym; continue; }
            short prev = 0; int rep;
            if (sym == 16) { if (n == 0) throw new InvalidDataException("inflate64 : répétition sans précédent"); prev = lengths[n - 1]; rep = 3 + s.Bits(2); }
            else if (sym == 17) rep = 3 + s.Bits(3);
            else rep = 11 + s.Bits(7);
            if (n + rep > nlen + ndist) throw new InvalidDataException("inflate64 : trop de longueurs");
            while (rep-- > 0) lengths[n++] = prev;
        }
        if (lengths[256] == 0) throw new InvalidDataException("inflate64 : pas de code de fin de bloc");
        var ll = new Huff(); int err = ll.Build(lengths, 0, nlen);
        if (err != 0 && (err < 0 || nlen != ll.Count[0] + ll.Count[1])) throw new InvalidDataException("inflate64 : codes littéraux invalides");
        var dd = new Huff(); err = dd.Build(lengths, nlen, ndist);
        if (err != 0 && (err < 0 || ndist != dd.Count[0] + dd.Count[1])) throw new InvalidDataException("inflate64 : codes de distance invalides");
        Codes(ref s, ll, dd);
    }

    static void Codes(ref State s, Huff lc, Huff dc)
    {
        int sym;
        do
        {
            sym = s.Decode(lc);
            if (sym < 256)
            {
                if (s.OutPos >= s.Out.Length) throw new InvalidDataException("inflate64 : sortie trop longue");
                s.Out[s.OutPos++] = (byte)sym;
            }
            else if (sym > 256)
            {
                sym -= 257;
                if (sym >= 29) throw new InvalidDataException("inflate64 : symbole de longueur invalide");
                int len = LBase[sym] + s.Bits(LExt[sym]);
                int ds = s.Decode(dc);
                if (ds >= 32) throw new InvalidDataException("inflate64 : symbole de distance invalide");
                int dist = DBase[ds] + s.Bits(DExt[ds]);
                if (dist > s.OutPos) throw new InvalidDataException("inflate64 : distance trop grande");
                if (s.OutPos + len > s.Out.Length) throw new InvalidDataException("inflate64 : sortie trop longue");
                for (int i = 0; i < len; i++, s.OutPos++) s.Out[s.OutPos] = s.Out[s.OutPos - dist];
            }
        } while (sym != 256);
    }
}
