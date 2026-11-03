/* CRC32C (Castagnoli) — algorithme du manifeste GameDataFileList.txt et de pkg.idx. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <nmmintrin.h>

/* Retourne le CRC32C du fichier, ou -1 s'il est illisible ; *size reçoit la taille. */
int64_t crc32c_file(const char *path, uint64_t *size)
{
    FILE *f = fopen(path, "rb");
    if (!f) return -1;
    uint8_t *buf = malloc(1 << 20);   /* pas de tampon statique : appelé depuis plusieurs threads */
    if (!buf) { fclose(f); return -1; }
    uint32_t c = 0xFFFFFFFFu;
    uint64_t total = 0;
    size_t n;
    while ((n = fread(buf, 1, (size_t)1 << 20, f)) > 0) {
        size_t i = 0;
        for (; i + 8 <= n; i += 8) c = (uint32_t)_mm_crc32_u64(c, *(uint64_t *)(buf + i));
        for (; i < n; i++) c = _mm_crc32_u8(c, buf[i]);
        total += n;
    }
    fclose(f);
    free(buf);
    *size = total;
    return (int64_t)(c ^ 0xFFFFFFFFu);
}

uint32_t crc32c_buf(const uint8_t *p, size_t n)
{
    uint32_t c = 0xFFFFFFFFu;
    for (size_t i = 0; i < n; i++) c = _mm_crc32_u8(c, p[i]);
    return c ^ 0xFFFFFFFFu;
}
