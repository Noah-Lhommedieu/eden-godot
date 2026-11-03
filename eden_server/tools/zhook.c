/* zhook.c — LD_PRELOAD inline-hook pour ZoneServer (ELF32, -nostdlib)
 * Patche a chaud CMSTCP{Channel,Connection}::{Encryption,Decryption}
 * et logue le plaintext de chaque trame dans /tmp/zhook.bin
 * Format record: u8 tag | u32 sec | u32 usec | u32 this | u32 len | data[len]
 *   tag: 'E' Encryption (sortant, plaintext=src), 'D' Decryption (entrant, plaintext=dst)
 * Build: gcc -m32 -nostdlib -shared -fPIC -fno-stack-protector -fno-builtin \
 *        -fcf-protection=none zhook.c -o zhook.so
 * Run:   LD_PRELOAD=./zhook.so ./ZoneServer
 */

typedef unsigned int u32;
typedef unsigned char u8;

/* ---- syscalls i386 ---- */
static int s_open(const char *p, int f, int m) {
    int r;
    asm volatile("int $0x80" : "=a"(r) : "a"(5), "b"(p), "c"(f), "d"(m) : "memory");
    return r;
}
static int s_write(int f, const void *b, int n) {
    int r;
    asm volatile("int $0x80" : "=a"(r) : "a"(4), "b"(f), "c"(b), "d"(n) : "memory");
    return r;
}
static int s_mprotect(void *a, int l, int p) {
    int r;
    asm volatile("int $0x80" : "=a"(r) : "a"(125), "b"(a), "c"(l), "d"(p) : "memory");
    return r;
}
static void s_gettime(u32 *sec, u32 *usec) {
    u32 tv[2];
    asm volatile("int $0x80" : "=a"(*(int *)0) : "a"(78), "b"(tv), "c"(0) : "memory");
    *sec = tv[0]; *usec = tv[1];
}

static int logfd = -1;

/* ---- logger ---- */
static void log_rec(u8 tag, u32 self, u32 len, const u8 *data) {
    u8 hdr[17];
    u32 s, u;
    if (logfd < 0) return;
    s_gettime(&s, &u);
    hdr[0] = tag;
    hdr[1] = s & 0xff; hdr[2] = (s >> 8) & 0xff; hdr[3] = (s >> 16) & 0xff; hdr[4] = (s >> 24) & 0xff;
    hdr[5] = u & 0xff; hdr[6] = (u >> 8) & 0xff; hdr[7] = (u >> 16) & 0xff; hdr[8] = (u >> 24) & 0xff;
    hdr[9] = self & 0xff; hdr[10] = (self >> 8) & 0xff; hdr[11] = (self >> 16) & 0xff; hdr[12] = (self >> 24) & 0xff;
    hdr[13] = len & 0xff; hdr[14] = (len >> 8) & 0xff; hdr[15] = (len >> 16) & 0xff; hdr[16] = (len >> 24) & 0xff;
    s_write(logfd, hdr, 17);
    if (len && len < 65536) s_write(logfd, data, len);
}

/* appelee avec args = {this,len,src,dst} */
__attribute__((used, noinline))
void c_log_enc(u32 *args) { log_rec('E', args[0], args[1], (u8 *)args[2]); }
__attribute__((used, noinline))
void c_log_dec(u32 *args) { log_rec('D', args[0], args[1], (u8 *)args[3]); }

/* ---- trampolines (remplis au load) ---- */
static u8 tramp_e_chan[16], tramp_d_chan[16], tramp_e_conn[16], tramp_d_conn[16];
static u32 ptr_e_chan, ptr_d_chan, ptr_e_conn, ptr_d_conn;

/* ---- hooks ----
 * Encryption(this,len,src,dst): log src, puis tail-jump trampoline.
 * Decryption(this,len,src,dst): call trampoline (orig s'execute), puis log dst.
 * A l'entree du hook: [esp]=ret, [esp+4..16]=a1..a4. Apres pusha: args a esp+36.
 */
__asm__(
    ".text\n"
    ".globl hook_e_chan\n"
    "hook_e_chan:\n"
    "    pushal\n"
    "    leal 36(%esp), %eax\n"
    "    pushl %eax\n"
    "    call c_log_enc\n"
    "    addl $4, %esp\n"
    "    popal\n"
    "    jmpl *ptr_e_chan\n"

    ".globl hook_d_chan\n"
    "hook_d_chan:\n"
    "    pushl 16(%esp)\n"
    "    pushl 16(%esp)\n"
    "    pushl 16(%esp)\n"
    "    pushl 16(%esp)\n"
    "    call *ptr_d_chan\n"
    "    addl $16, %esp\n"
    "    pushal\n"
    "    leal 36(%esp), %eax\n"
    "    pushl %eax\n"
    "    call c_log_dec\n"
    "    addl $4, %esp\n"
    "    popal\n"
    "    ret\n"

    ".globl hook_e_conn\n"
    "hook_e_conn:\n"
    "    pushal\n"
    "    leal 36(%esp), %eax\n"
    "    pushl %eax\n"
    "    call c_log_enc\n"
    "    addl $4, %esp\n"
    "    popal\n"
    "    jmpl *ptr_e_conn\n"

    ".globl hook_d_conn\n"
    "hook_d_conn:\n"
    "    pushl 16(%esp)\n"
    "    pushl 16(%esp)\n"
    "    pushl 16(%esp)\n"
    "    pushl 16(%esp)\n"
    "    call *ptr_d_conn\n"
    "    addl $16, %esp\n"
    "    pushal\n"
    "    leal 36(%esp), %eax\n"
    "    pushl %eax\n"
    "    call c_log_dec\n"
    "    addl $4, %esp\n"
    "    popal\n"
    "    ret\n"
);

extern char hook_e_chan[], hook_d_chan[], hook_e_conn[], hook_d_conn[];

static void patch_fn(u32 target, u8 *trampbuf, void *hook, u32 *ptrvar) {
    int i;
    for (i = 0; i < 6; i++) trampbuf[i] = ((u8 *)target)[i];
    trampbuf[6] = 0xe9;
    *(u32 *)(trampbuf + 7) = (target + 6) - ((u32)trampbuf + 11);
    s_mprotect((void *)((u32)trampbuf & ~0xfffU), 4096, 7);
    s_mprotect((void *)(target & ~0xfffU), 4096, 7);
    ((u8 *)target)[0] = 0xe9;
    *(u32 *)(target + 1) = (u32)hook - (target + 5);
    ((u8 *)target)[5] = 0x90;
    *ptrvar = (u32)trampbuf;
}

__attribute__((constructor))
static void init(void) {
    /* O_WRONLY|O_CREAT|O_APPEND = 0x241 */
    logfd = s_open("/tmp/zhook.bin", 0x241, 0666);
    patch_fn(0x868a440, tramp_e_chan, hook_e_chan, &ptr_e_chan);
    patch_fn(0x868a420, tramp_d_chan, hook_d_chan, &ptr_d_chan);
    patch_fn(0x868a400, tramp_e_conn, hook_e_conn, &ptr_e_conn);
    patch_fn(0x868a3e0, tramp_d_conn, hook_d_conn, &ptr_d_conn);
    /* marqueur de demarrage */
    log_rec('S', 0, 0, (const u8 *)"");
}
