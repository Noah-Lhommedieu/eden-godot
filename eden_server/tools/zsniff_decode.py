#!/usr/bin/env python3
# Decodeur passif client<->ZoneServer Eden depuis un pcap (tcpdump -i lo port X)
# Usage: tcpdump -i lo -w /tmp/z.pcap 'tcp port 10022'  puis  python3 zsniff_decode.py /tmp/z.pcap
import os, struct, sys, time

HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    '..', 'server_us', 'ZoneServer') + os.sep
N = int(open(HERE + 'keygen_n').read().strip(), 16)
P = int(open(HERE + 'keygen_p').read().strip(), 16)
E = int(open(HERE + 'keygen_e').read().strip(), 16)
Q = N // P
D = pow(E, -1, (P - 1) * (Q - 1))


def rsa_decrypt(ct):
    m = pow(int.from_bytes(ct, 'big'), D, N)
    em = m.to_bytes(256, 'big')
    i = em.find(b'\x00', 2)
    return em[i + 1:]


class RC4:
    def __init__(self, key):
        S = list(range(256)); j = 0
        for i in range(256):
            j = (j + S[i] + key[i % len(key)]) & 0xff
            S[i], S[j] = S[j], S[i]
        self.S, self.i, self.j = S, 0, 0

    def crypt(self, data):
        S = self.S; i, j = self.i, self.j; out = bytearray()
        for b in data:
            i = (i + 1) & 0xff; j = (j + S[i]) & 0xff
            S[i], S[j] = S[j], S[i]
            out.append(b ^ S[(S[i] + S[j]) & 0xff])
        self.i, self.j = i, j
        return bytes(out)


def read_pcap(path):
    """Yield (ts, ip_src, port_src, ip_dst, port_dst, seq, payload) en ordre."""
    data = open(path, 'rb').read()
    magic = struct.unpack('<I', data[:4])[0]
    nano = magic in (0xa1b23c4d, 0x4d3cb2a1)
    off = 24
    while off + 16 <= len(data):
        ts_sec, ts_frac, incl, orig = struct.unpack('<IIII', data[off:off + 16])
        off += 16
        pkt = data[off:off + incl]; off += incl
        ts = ts_sec + ts_frac / (1e9 if nano else 1e6)
        # linux cooked (SLL) ou loopback ou ethernet
        if len(pkt) < 16:
            continue
        dl = None
        if pkt[:2] == b'\x00\x00' or pkt[:2] == b'\x02\x00':  # SLL
            proto = struct.unpack('>H', pkt[14:16])[0]
            ipoff = 16
        elif pkt[:4] in (b'\x02\x00\x00\x00', b'\x00\x00\x00\x00'):
            # BSD loopback: family u32
            ipoff = 4; proto = pkt[3] if pkt[0] == 0 else pkt[0]
            proto = 0x0800
        else:  # ethernet
            proto = struct.unpack('>H', pkt[12:14])[0]
            ipoff = 14
        if proto != 0x0800 or len(pkt) < ipoff + 20:
            continue
        ihl = (pkt[ipoff] & 0x0f) * 4
        if pkt[ipoff + 9] != 6:
            continue
        src = '.'.join(map(str, pkt[ipoff + 12:ipoff + 16]))
        dst = '.'.join(map(str, pkt[ipoff + 16:ipoff + 20]))
        toff = ipoff + ihl
        if len(pkt) < toff + 20:
            continue
        sp, dp, seq = struct.unpack('>HHI', pkt[toff:toff + 8])
        doff = (pkt[toff + 12] >> 4) * 4
        flags = pkt[toff + 13]
        payload = pkt[toff + doff:]
        yield ts, src, sp, dst, dp, seq, flags, payload


class Stream:
    def __init__(self):
        self.segs = []  # (seq, data)

    def add(self, seq, data):
        if data:
            self.segs.append((seq, data))

    def reassemble(self):
        """Fusionne les segments par seq, ignore retransmissions."""
        if not self.segs:
            return b''
        self.segs.sort()
        base = self.segs[0][0]
        buf = bytearray(); end = base
        for seq, d in self.segs:
            if seq < end:
                d = d[end - seq:]
                seq = end
            if seq > end:
                buf += b'\x00' * (seq - end)  # trou (perte)
                end = seq
            buf += d; end += len(d)
        return bytes(buf)


def frames(stream, rd):
    """Decode [u16 len][rc4] -> liste de (offset, plain)."""
    out = []; buf = stream; pos = 0
    while pos + 2 <= len(buf):
        ln = struct.unpack('<H', buf[pos:pos + 2])[0]
        if pos + 2 + ln > len(buf):
            break
        enc = buf[pos + 2:pos + 2 + ln]
        out.append((pos, rd.crypt(enc)))
        pos += 2 + ln
    return out


def main(path):
    conns = {}  # (cliip,cliport) -> {c2s:Stream, s2c:Stream}
    for ts, src, sp, dst, dp, seq, flags, payload in read_pcap(path):
        if dp == ZONE_PORT:
            key = (src, sp)
            c = conns.setdefault(key, {'c2s': Stream(), 's2c': Stream(), 't0': ts})
            c['c2s'].add(seq, payload)
        elif sp == ZONE_PORT:
            key = (dst, dp)
            c = conns.setdefault(key, {'c2s': Stream(), 's2c': Stream(), 't0': ts})
            c['s2c'].add(seq, payload)

    for key, c in conns.items():
        s2c = c['s2c'].reassemble()
        c2s = c['c2s'].reassemble()
        if len(s2c) < 8 or len(c2s) < 256:
            continue
        # handshake serveur: u32 nlen, u32 elen, n, e
        nlen, elen = struct.unpack('<II', s2c[:8])
        hs = 8 + nlen + elen
        wn = s2c[8:8 + nlen]
        if int.from_bytes(wn, 'big') != N:
            print(f'[warn] {key} cle n differente (autre serveur?)', file=sys.stderr)
        blob = c2s[:256]
        try:
            key_b = rsa_decrypt(blob)
        except Exception as ex:
            print(f'[err] {key} RSA: {ex}', file=sys.stderr)
            continue
        print(f'=== connexion {key[0]}:{key[1]}  t0={time.strftime("%H:%M:%S", time.localtime(c["t0"]))} key={key_b.hex()}')
        c_rd, s_rd = RC4(key_b), RC4(key_b)
        # offset dans les flux reconstitues -> timestamps approx (premier segment >= offset)
        c_ev = frames(c2s[256:], c_rd)
        s_ev = frames(s2c[hs:], s_rd)
        for tag, evs in (('C>S', c_ev), ('S>C', s_ev)):
            for off, plain in evs:
                ncid = struct.unpack('<H', plain[:2])[0] if len(plain) >= 2 else -1
                print(f'  {tag} NCID={ncid} len={len(plain)} {plain.hex()}')


if __name__ == '__main__':
    ZONE_PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 10022
    main(sys.argv[1])
