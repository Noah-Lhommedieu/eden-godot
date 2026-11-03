#!/usr/bin/env python3
"""ptrace mini-debugger : dump le plaintext de tout le trafic de ZoneServer.
   Breakpoints int3 sur CMSTCP{Channel,Connection}::{Encryption,Decryption}.
   Encryption(this,len,src,dst) -> plaintext = src (log direct).
   Decryption(this,len,src,dst) -> plaintext = RC4(key=this+off, src) fait dans le tracer.
   Usage: python3 ptrace_sniff.py <pid> <outfile>
"""
import ctypes, ctypes.util, os, signal, struct, sys, time

libc = ctypes.CDLL(ctypes.util.find_library("c"), use_errno=True)
PTRACE_PEEKDATA, PTRACE_POKEDATA = 2, 5
PTRACE_CONT, PTRACE_ATTACH, PTRACE_DETACH = 7, 16, 17
PTRACE_SINGLESTEP, PTRACE_GETREGS, PTRACE_SETREGS = 9, 12, 13
PTRACE_SETOPTIONS, PTRACE_GETEVENTMSG = 0x4200, 0x4201
PTRACE_GETREGSET, PTRACE_SETREGSET = 0x4204, 0x4205
NT_PRSTATUS = 1
PTRACE_O_TRACECLONE = 0x00000080
__WALL = 0x40000000

class regs(ctypes.Structure):
    """user_regs_struct i386 (17 x u32) tel que rendu par GETREGSET/NT_PRSTATUS
       pour un tracee 32-bit."""
    _fields_ = [(n, ctypes.c_uint32) for n in
        ("ebx","ecx","edx","esi","edi","ebp","eax","ds","es","fs","gs",
         "orig_eax","eip","cs","eflags","esp","ss")]

class iovec(ctypes.Structure):
    _fields_ = [("iov_base", ctypes.c_void_p), ("iov_len", ctypes.c_size_t)]

# addr -> (tag, rc4 key offset si 'D', sinon 0; 'R' = RC4 brut)
BPS = {0x868a440: ('E', 0),        # CMSTCPChannel::Encryption
       0x868a420: ('D', 0x2498),   # CMSTCPChannel::Decryption
       0x868a400: ('E', 0),        # CMSTCPConnection::Encryption
       0x868a3e0: ('D', 0x24a4),   # CMSTCPConnection::Decryption
       0x86a16a0: ('R', 0)}        # RC4(key,len,src,dst) — validation bp

def ptrace(req, pid, addr=0, data=0):
    libc.ptrace.restype = ctypes.c_long
    ctypes.set_errno(0)
    r = libc.ptrace(req, pid, ctypes.c_void_p(addr), ctypes.c_void_p(data))
    e = ctypes.get_errno()
    if r == -1 and e: raise OSError(e, "ptrace req=%d" % req)
    return r

def peek64(pid, addr):
    libc.ptrace.restype = ctypes.c_long
    ctypes.set_errno(0)
    r = libc.ptrace(PTRACE_PEEKDATA, pid, ctypes.c_void_p(addr), None)
    e = ctypes.get_errno()
    if r == -1 and e: raise OSError(e, "peek %x" % addr)
    return r & 0xFFFFFFFFFFFFFFFF

def peek(pid, addr):
    return peek64(pid, addr) & 0xFFFFFFFF

def poke32(pid, addr, low32):
    cur = peek64(pid, addr)
    val = (cur & 0xFFFFFFFF00000000) | (low32 & 0xFFFFFFFF)
    libc.ptrace.restype = ctypes.c_long
    ctypes.set_errno(0)
    r = libc.ptrace(PTRACE_POKEDATA, pid, ctypes.c_void_p(addr), ctypes.c_void_p(val))
    e = ctypes.get_errno()
    if r == -1 and e: raise OSError(e, "poke %x" % addr)

def read_mem(pid, addr, n):
    out = bytearray()
    while n > 0:
        try: w = peek64(pid, addr)
        except OSError: break
        take = min(8, n)
        out += struct.pack("<Q", w)[:take]
        addr += take; n -= take
    return bytes(out)

def getregs(pid):
    r = regs()
    io = iovec(ctypes.cast(ctypes.byref(r), ctypes.c_void_p), ctypes.sizeof(r))
    libc.ptrace.restype = ctypes.c_long
    ctypes.set_errno(0)
    if libc.ptrace(PTRACE_GETREGSET, pid, NT_PRSTATUS, ctypes.byref(io)) == -1:
        raise OSError(ctypes.get_errno())
    return r

def setregs(pid, r):
    io = iovec(ctypes.cast(ctypes.byref(r), ctypes.c_void_p), ctypes.sizeof(r))
    libc.ptrace.restype = ctypes.c_long
    ctypes.set_errno(0)
    if libc.ptrace(PTRACE_SETREGSET, pid, NT_PRSTATUS, ctypes.byref(io)) == -1:
        raise OSError(ctypes.get_errno())

def rc4_apply(sb, data):
    """sb = 1032 octets d'un RC4_KEY openssl (x,y,data[256])."""
    if len(sb) < 1032: return data
    x, y = struct.unpack("<II", sb[:8])
    S = list(struct.unpack("<256I", sb[8:1032]))
    out = bytearray(len(data))
    for i, b in enumerate(data):
        x = (x + 1) & 0xFF; y = (y + S[x]) & 0xFF
        S[x], S[y] = S[y], S[x]
        out[i] = b ^ S[(S[x] + S[y]) & 0xFF]
    return bytes(out)

saved_bps = {}
out = None
dbg = lambda m: print(m, file=sys.stderr, flush=True)

def ts():
    return time.strftime("%H:%M:%S") + f".{int(time.time()*1000)%1000:03d}"

def dump(tag, chan, data):
    ncid = struct.unpack('<H', data[:2])[0] if len(data) >= 2 else -1
    out.write(f"[{ts()}] {tag} ch={chan:08x} NCID={ncid} len={len(data)} {data.hex()}\n".encode())

def step_over(tid, addr):
    r = getregs(tid)
    poke32(tid, addr, saved_bps[addr])
    r.eip = addr
    setregs(tid, r)
    ptrace(PTRACE_SINGLESTEP, tid)
    _, st = os.waitpid(tid, __WALL)
    if not (os.WIFSTOPPED(st) and os.WSTOPSIG(st) == signal.SIGTRAP):
        dbg(f"step status inattendu: {st:#x}")
    poke32(tid, addr, (saved_bps[addr] & 0xFFFFFF00) | 0xCC)

hitcount = [0]

def on_bp(tid, addr):
    tag, keyoff = BPS[addr]
    esp = getregs(tid).esp
    if tag == 'R':
        # RC4(key, len, src, dst)
        ln  = peek(tid, esp + 8)
        src = peek(tid, esp + 12)
        hitcount[0] += 1
        if 0 < ln <= 65536:
            data = read_mem(tid, src, ln)
            out.write(f"[{ts()}] R len={ln} {data[:64].hex()}\n".encode())
        step_over(tid, addr)
        return
    a_this = peek(tid, esp + 4)
    ln     = peek(tid, esp + 8)
    src    = peek(tid, esp + 12)
    hitcount[0] += 1
    if 0 < ln <= 65536:
        data = read_mem(tid, src, ln)
        if tag == 'D':
            kb = read_mem(tid, a_this + keyoff, 1032)
            data = rc4_apply(kb, data)
        dump(tag, a_this, data)
    step_over(tid, addr)

def main():
    global out
    pid, out_path = int(sys.argv[1]), sys.argv[2]
    out = open(out_path, "ab", buffering=0)

    tids = [int(t) for t in os.listdir(f"/proc/{pid}/task")]
    for tid in tids:
        try:
            ptrace(PTRACE_ATTACH, tid)
            _, st = os.waitpid(tid, __WALL)
            dbg(f"attach {tid} stop sig={os.WSTOPSIG(st) if os.WIFSTOPPED(st) else '?'}")
        except OSError as e:
            dbg(f"attach {tid}: {e}")

    for addr in BPS:
        w = peek(pid, addr)
        saved_bps[addr] = w
        poke32(pid, addr, (w & 0xFFFFFF00) | 0xCC)
    dbg(f"bps poses: {[hex(a) for a in BPS]}")

    for tid in tids:
        try:
            ptrace(PTRACE_SETOPTIONS, tid, 0, PTRACE_O_TRACECLONE)
            ptrace(PTRACE_CONT, tid)
        except OSError as e:
            dbg(f"cont {tid}: {e}")

    while True:
        try: wpid, status = os.waitpid(-1, __WALL)
        except ChildProcessError:
            dbg("plus de tracee"); break
        if os.WIFEXITED(status) or os.WIFSIGNALED(status):
            dbg(f"pid {wpid} termine sig={os.WTERMSIG(status) if os.WIFSIGNALED(status) else '-'}")
            if wpid == pid: break
            continue
        if not os.WIFSTOPPED(status):
            continue
        sig = os.WSTOPSIG(status)
        ev = (status >> 16) & 0xff
        if sig != signal.SIGTRAP:
            try: ptrace(PTRACE_CONT, wpid, 0, sig)
            except OSError: pass
            continue
        try: r = getregs(wpid)
        except OSError:
            continue
        hit = r.eip - 1
        try:
            if hit in BPS:
                on_bp(wpid, hit)
            elif ev != 0:
                pass  # clone event etc
            else:
                dbg(f"trap inattendu tid={wpid} eip={r.eip:#x}")
        except OSError as e:
            dbg(f"handler err tid={wpid}: {e}")
        try: ptrace(PTRACE_CONT, wpid)
        except OSError: pass

main()
