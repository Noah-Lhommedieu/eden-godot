/* Inflate à fenêtre 64 Ko (variante zlib du client EDENFR), dérivé de l'algorithme de puff.c :
 * deflate standard + codes de distance 30 et 31 (base 32769/49153, 14 bits extra).
 * Mode len64=1 : longueur 285 = base 3, 16 bits extra (Deflate64 officiel) ; sinon 258 (zlib). */
#include <stdint.h>
#include <string.h>
#include <stddef.h>

typedef struct { const uint8_t *in; size_t inlen, incnt; uint32_t bitbuf; int bitcnt;
                 uint8_t *out; size_t outlen, outcnt; int len64; } S;
typedef struct { short count[16]; short symbol[288]; } H;

static int bits(S *s, int need)
{
    uint32_t v = s->bitbuf;
    while (s->bitcnt < need) {
        if (s->incnt >= s->inlen) return -1;
        v |= (uint32_t)s->in[s->incnt++] << s->bitcnt;
        s->bitcnt += 8;
    }
    s->bitbuf = v >> need;
    s->bitcnt -= need;
    return (int)(v & ((1u << need) - 1));
}

static int construct(H *h, const short *length, int n)
{
    short offs[16];
    int left = 1;
    for (int l = 0; l <= 15; l++) h->count[l] = 0;
    for (int i = 0; i < n; i++) h->count[length[i]]++;
    if (h->count[0] == n) return 0;
    for (int l = 1; l <= 15; l++) { left <<= 1; left -= h->count[l]; if (left < 0) return left; }
    offs[1] = 0;
    for (int l = 1; l < 15; l++) offs[l + 1] = offs[l] + h->count[l];
    for (int i = 0; i < n; i++) if (length[i]) h->symbol[offs[length[i]]++] = i;
    return left;
}

static int decode(S *s, const H *h)
{
    int code = 0, first = 0, index = 0;
    for (int len = 1; len <= 15; len++) {
        int b = bits(s, 1); if (b < 0) return -1;
        code |= b;
        int count = h->count[len];
        if (code - count < first) return h->symbol[index + (code - first)];
        index += count; first += count; first <<= 1; code <<= 1;
    }
    return -2;
}

static const short lbase[29] = {3,4,5,6,7,8,9,10,11,13,15,17,19,23,27,31,35,43,51,59,67,83,99,115,131,163,195,227,258};
static const short lext[29] = {0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,0};
static const int dbase[32] = {1,2,3,4,5,7,9,13,17,25,33,49,65,97,129,193,257,385,513,769,1025,1537,2049,3073,4097,6145,8193,12289,16385,24577,32769,49153};
static const short dext[32] = {0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10,11,11,12,12,13,13,14,14};

static int codes(S *s, const H *lc, const H *dc)
{
    int sym;
    do {
        sym = decode(s, lc);
        if (sym < 0) return sym;
        if (sym < 256) {
            if (s->outcnt >= s->outlen) return 1;
            s->out[s->outcnt++] = (uint8_t)sym;
        } else if (sym > 256) {
            sym -= 257;
            if (sym >= 29) return -10;
            int len, e = 0;
            if (sym == 28 && s->len64) { len = 3; e = 16; } else { len = lbase[sym]; e = lext[sym]; }
            int x = bits(s, e); if (x < 0) return -1;
            len += x;
            sym = decode(s, dc); if (sym < 0) return sym;
            if (sym >= 32) return -10;
            x = bits(s, dext[sym]); if (x < 0) return -1;
            size_t dist = (size_t)dbase[sym] + x;
            if (dist > s->outcnt) return -11;
            if (s->outcnt + len > s->outlen) return 1;
            for (int i = 0; i < len; i++, s->outcnt++) s->out[s->outcnt] = s->out[s->outcnt - dist];
        }
    } while (sym != 256);
    return 0;
}

static int stored(S *s)
{
    s->bitbuf = 0; s->bitcnt = 0;
    if (s->incnt + 4 > s->inlen) return -1;
    unsigned len = s->in[s->incnt] | (s->in[s->incnt + 1] << 8);
    unsigned nlen = s->in[s->incnt + 2] | (s->in[s->incnt + 3] << 8);
    s->incnt += 4;
    if (len != (~nlen & 0xffff)) return -12;
    if (s->incnt + len > s->inlen) return -1;
    if (s->outcnt + len > s->outlen) return 1;
    memcpy(s->out + s->outcnt, s->in + s->incnt, len);
    s->incnt += len; s->outcnt += len;
    return 0;
}

static H fl, fd;
static int fixed(S *s)
{
    static int built;
    if (!built) {
        short l[288]; int i;
        for (i = 0; i < 144; i++) l[i] = 8;
        for (; i < 256; i++) l[i] = 9;
        for (; i < 280; i++) l[i] = 7;
        for (; i < 288; i++) l[i] = 8;
        construct(&fl, l, 288);
        for (i = 0; i < 32; i++) l[i] = 5;
        construct(&fd, l, 32);
        built = 1;
    }
    return codes(s, &fl, &fd);
}

static int dynamic(S *s)
{
    static const short order[19] = {16,17,18,0,8,7,9,6,10,5,11,4,12,3,13,2,14,1,15};
    short lengths[320]; H lc, dc;
    int nlen = bits(s, 5) + 257, ndist = bits(s, 5) + 1, ncode = bits(s, 4) + 4;
    if (nlen > 288 || ndist > 32) return -13;
    int i;
    for (i = 0; i < ncode; i++) { int b = bits(s, 3); if (b < 0) return -1; lengths[order[i]] = b; }
    for (; i < 19; i++) lengths[order[i]] = 0;
    if (construct(&lc, lengths, 19) != 0) return -14;
    i = 0;
    while (i < nlen + ndist) {
        int sym = decode(s, &lc); if (sym < 0) return sym;
        if (sym < 16) lengths[i++] = sym;
        else {
            int len = 0, rep;
            if (sym == 16) { if (i == 0) return -15; len = lengths[i - 1]; rep = 3 + bits(s, 2); }
            else if (sym == 17) rep = 3 + bits(s, 3); else rep = 11 + bits(s, 7);
            if (i + rep > nlen + ndist) return -16;
            while (rep--) lengths[i++] = len;
        }
    }
    if (lengths[256] == 0) return -17;
    int err = construct(&lc, lengths, nlen);
    if (err && (err < 0 || nlen != lc.count[0] + lc.count[1])) return -18;
    err = construct(&dc, lengths + nlen, ndist);
    if (err && (err < 0 || ndist != dc.count[0] + dc.count[1])) return -19;
    return codes(s, &lc, &dc);
}

/* Retourne 0 si OK (*outlen = octets produits), <0 erreur, 1 si le tampon est trop petit. */
int inflate64(const uint8_t *in, size_t inlen, uint8_t *out, size_t *outlen, int len64)
{
    S s = { in, inlen, 0, 0, 0, out, *outlen, 0, len64 };
    int last, err;
    do {
        last = bits(&s, 1); int type = bits(&s, 2);
        if (last < 0 || type < 0) { err = -1; break; }
        err = type == 0 ? stored(&s) : type == 1 ? fixed(&s) : type == 2 ? dynamic(&s) : -20;
        if (err) break;
    } while (!last);
    *outlen = s.outcnt;
    return err;
}
