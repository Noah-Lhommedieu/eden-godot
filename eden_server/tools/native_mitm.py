#!/usr/bin/env python3
# MITM déchiffrant Eden : client -> proxy -> serveurs
#   :6543  -> LoginServer natif :6544
#   :5200  -> WorldServer C#    :5300
#   :10122 -> ZoneServer natif  :10022
# Log chaque NCID déchiffré dans les deux sens sur stdout.
#
# Framing : les binaires natifs parlent [u16 len] (oracle historique,
# inchangé). Le serveur C# parle [varint LEB128 len] depuis le cutover
# phase-4b. EDEN_MITM_FRAME=u16 (défaut, capture native) | varint.
import socket, struct, threading, os, time

FRAME = os.environ.get('EDEN_MITM_FRAME', 'u16')

HERE = os.path.dirname(os.path.abspath(__file__))
N = int(open(os.path.join(HERE, 'rsa_n.txt')).read().strip(), 16)
D = int(open(os.path.join(HERE, 'rsa_d.txt')).read().strip(), 16)
NBYTES = N.to_bytes(256, 'big')
BLOB = struct.pack('<II', 256, 2) + NBYTES + b'\x01\x01'  # e=257


def rsa_decrypt(ct):
    m = pow(int.from_bytes(ct, 'big'), D, N)
    em = m.to_bytes(256, 'big')
    i = em.find(b'\x00', 2)
    return em[i + 1:]


def rsa_encrypt(msg, n_int, e_int):
    k = (n_int.bit_length() + 7) // 8
    ps_len = k - 3 - len(msg)
    ps = b''
    while len(ps) < ps_len:
        ps += os.urandom(ps_len - len(ps)).replace(b'\x00', b'')
    ps = ps[:ps_len]
    em = b'\x00\x02' + ps + b'\x00' + msg
    c = pow(int.from_bytes(em, 'big'), e_int, n_int)
    return c.to_bytes(k, 'big')


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


def log(tag, data):
    t = time.strftime('%H:%M:%S') + f'.{int(time.time() * 1000) % 1000:03d}'
    ncid = struct.unpack('<H', data[:2])[0] if len(data) >= 2 else -1
    print(f'[{t}] {tag} NCID={ncid} len={len(data)} {data.hex()}', flush=True)


def encode_len(n):
    """Préfixe de trame selon EDEN_MITM_FRAME : u16 LE ou varint LEB128."""
    if FRAME == 'varint':
        out = bytearray()
        while True:
            b = n & 0x7F
            n >>= 7
            out.append(b | 0x80 if n else b)
            if not n:
                return bytes(out)
    return struct.pack('<H', n)


def decode_len(buf):
    """-> (len, octets préfixe) ou None si préfixe incomplet.
    ValueError si varint overlong / > u32."""
    if FRAME == 'varint':
        val = 0
        for i, b in enumerate(buf[:5]):
            if i == 4 and b & 0x70:
                raise ValueError('varint > u32')
            val |= (b & 0x7F) << (7 * i)
            if not b & 0x80:
                return val, i + 1
        if len(buf) >= 5:
            raise ValueError('varint overlong')
        return None
    if len(buf) < 2:
        return None
    return struct.unpack('<H', buf[:2])[0], 2


def pump(src, dst, rd, wr, tag, stop, pfx=''):
    """lit frames [len][enc], déchiffre rd, log, chiffre wr, envoie dst"""
    buf = b''
    try:
        while not stop[0]:
            chunk = src.recv(65536)
            if not chunk:
                break
            buf += chunk
            while True:
                res = decode_len(buf)
                if res is None:
                    break
                ln, hdr = res
                if len(buf) < hdr + ln:
                    break
                enc = buf[hdr:hdr + ln]; buf = buf[hdr + ln:]
                plain = bytearray(rd.crypt(enc))
                log(pfx + tag, bytes(plain))
                dst.sendall(encode_len(ln) + wr.crypt(bytes(plain)))
    except Exception as e:
        print(f'{pfx}{tag} pump err: {e}', flush=True)
    stop[0] = True
    try:
        dst.shutdown(socket.SHUT_RDWR)
    except Exception:
        pass


def recvall(s, n):
    d = b''
    while len(d) < n:
        c = s.recv(n - len(d))
        if not c:
            raise ConnectionError('closed')
        d += c
    return d


def mitm(client, target, pfx=''):
    print(f'=== connexion {pfx or "client"} ===', flush=True)
    try:
        client.sendall(BLOB)
        key = rsa_decrypt(recvall(client, 256))
        print(f'[mitm] {pfx}clé RC4 client: {key.hex()}', flush=True)
        c_rd, c_wr = RC4(key), RC4(key)

        srv = socket.create_connection(('127.0.0.1', target), timeout=5)
        blob = recvall(srv, 266)
        nlen, elen = struct.unpack('<II', blob[:8])
        wn, we = blob[8:8 + nlen], blob[8 + nlen:8 + nlen + elen]
        print(f'[mitm] {pfx}RSA natif nlen={nlen} elen={elen} e={we.hex()}', flush=True)
        wkey = os.urandom(5)
        srv.sendall(rsa_encrypt(wkey, int.from_bytes(wn, 'big'), int.from_bytes(we, 'big')))
        w_rd, w_wr = RC4(wkey), RC4(wkey)
        srv.settimeout(None)

        stop = [False]
        threading.Thread(target=pump, args=(client, srv, c_rd, w_wr, 'C>S', stop, pfx), daemon=True).start()
        pump(srv, client, w_rd, c_wr, 'S>C', stop, pfx)
    except Exception as e:
        print(f'{pfx}mitm err: {e}', flush=True)
    finally:
        client.close()


def proxy(listen_port, target_port, pfx=''):
    srv = socket.socket(); srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(('0.0.0.0', listen_port)); srv.listen(5)
    print(f'[mitm] écoute :{listen_port} -> :{target_port} ({pfx or "client"})', flush=True)
    while True:
        c, a = srv.accept()
        threading.Thread(target=mitm, args=(c, target_port, pfx), daemon=True).start()


def raw_pump(src, dst, tag, stop):
    """Relais brut pour liens inter-serveurs (non chiffrés) : dump hex + tentative parse [len][u16 ncid]."""
    buf = b''
    try:
        while not stop[0]:
            chunk = src.recv(65536)
            if not chunk:
                break
            t = time.strftime('%H:%M:%S') + f'.{int(time.time() * 1000) % 1000:03d}'
            print(f'[{t}] {tag} RAW {chunk.hex()}', flush=True)
            buf += chunk
            while True:
                try:
                    res = decode_len(buf)
                except ValueError:
                    break
                if res is None:
                    break
                ln, hdr = res
                # ln<2 = trame sans NCID possible (invalide côté
                # serveur) — on arrête le parse heuristique là.
                if 2 <= ln <= 60000 and len(buf) >= hdr + ln:
                    ncid = struct.unpack('<H', buf[hdr:hdr + 2])[0]
                    print(f'[{t}] {tag} FRAME ncid={ncid} len={ln} {buf[hdr:hdr+ln].hex()}', flush=True)
                    buf = buf[hdr + ln:]
                else:
                    break
            dst.sendall(chunk)
    except Exception as e:
        print(f'{tag} raw err: {e}', flush=True)
    stop[0] = True
    try:
        dst.shutdown(socket.SHUT_RDWR)
    except Exception:
        pass


def raw_proxy(listen_port, target_port, tag):
    srv = socket.socket(); srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(('0.0.0.0', listen_port)); srv.listen(5)
    print(f'[mitm] raw :{listen_port} -> :{target_port} ({tag})', flush=True)
    while True:
        c, a = srv.accept()
        try:
            s = socket.create_connection(('127.0.0.1', target_port))
        except Exception as e:
            print(f'{tag} connect err: {e}', flush=True)
            c.close()
            continue
        stop = [False]
        print(f'=== {tag} connexion ===', flush=True)
        threading.Thread(target=raw_pump, args=(c, s, tag + '_C>S', stop), daemon=True).start()
        threading.Thread(target=raw_pump, args=(s, c, tag + '_S>C', stop), daemon=True).start()


threading.Thread(target=proxy, args=(6543, 6544), daemon=True).start()
threading.Thread(target=proxy, args=(10122, 10022, 'Z_'), daemon=True).start()
threading.Thread(target=proxy, args=(2068, 2067, 'T_'), daemon=True).start()
threading.Thread(target=proxy, args=(5711, int(os.environ.get('ZW_TARGET', '5710')), 'ZW_'), daemon=True).start()
proxy(5200, int(os.environ.get('WORLD_TARGET', '5300')), 'W_')
