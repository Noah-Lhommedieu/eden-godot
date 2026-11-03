#!/usr/bin/env python3
"""Session headless Eden complète : ticket T_ forgé + entrée zone.

Contrairement à eden_headless.py --replay (ticket périmé → rejet T_),
ce script obtient un VRAI ticket auprès du TicketServer (:2067) via
ServerGetTicket (NCID 0 → 2), puis rejoue la séquence d'entrée de la
capture en remplaçant le NCID 0 par le ticket frais.

Flux :
    T_  : RSA blob → RC4 → ServerGetTicket [id][id][cip][sip][port]
        → ClientGetTicket [+t1][+t2] → ticket 22B = [id][cip][sip]
          [port][t1][t2]
    Zone: RSA blob → RC4 → NCID 0 [ticket 22B] → 473 ClientHello
        → rejeu des D de la capture (burst + MigrationDone NCID 4)
        → écoute des trames serveur (log des NCIDs reçus)

Usage :
    tools/eden_session.py --char-id 200000002 --listen 20 \\
        --out /tmp/sess1.log
    tools/eden_session.py --char-id 200000003 --send-move \\
        --out /tmp/sess2.log
"""
import argparse
import os
import re
import socket
import struct
import sys
import threading
import time

LINE = re.compile(
    r'^\[[\d:]+\.\d+\]\s+([EDS])\s+(?:ch=([0-9a-f]+)\s+)?'
    r'NCID=(\d+)\s+len=(\d+)\s+([0-9a-fA-F]+)\s*$')

CAPTURE = os.path.join(os.path.dirname(__file__), '..', 'captures',
                       'zsniff_cs_entry_success.bin')


class RC4:
    def __init__(self, key: bytes):
        s = list(range(256))
        j = 0
        for i in range(256):
            j = (j + s[i] + key[i % len(key)]) & 0xff
            s[i], s[j] = s[j], s[i]
        self.s, self.i, self.j = s, 0, 0

    def crypt(self, data: bytes) -> bytes:
        s, i, j = self.s, self.i, self.j
        out = bytearray()
        for b in data:
            i = (i + 1) & 0xff
            j = (j + s[i]) & 0xff
            s[i], s[j] = s[j], s[i]
            out.append(b ^ s[(s[i] + s[j]) & 0xff])
        self.i, self.j = i, j
        return bytes(out)


def recv_exact(sock: socket.socket, n: int) -> bytes:
    buf = b''
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError('connexion fermée')
        buf += chunk
    return buf


def rsa_public_encrypt(key: bytes, n: int, e: int) -> bytes:
    k = (n.bit_length() + 7) // 8
    ps = bytearray()
    while len(ps) < k - len(key) - 3:
        b = os.urandom(1)
        if b != b'\x00':
            ps += b
    em = b'\x00\x02' + bytes(ps) + b'\x00' + key
    return pow(int.from_bytes(em, 'big'), e, n).to_bytes(k, 'big')


def handshake(sock: socket.socket) -> tuple[RC4, RC4]:
    hdr = recv_exact(sock, 8)
    n_len = int.from_bytes(hdr[0:4], 'little')
    e_len = int.from_bytes(hdr[4:8], 'little')
    n = int.from_bytes(recv_exact(sock, n_len), 'big')
    e = int.from_bytes(recv_exact(sock, e_len), 'big')
    key = os.urandom(5)
    sock.sendall(rsa_public_encrypt(key, n, e))
    return RC4(key), RC4(key)


def encode_varint(n: int) -> bytes:
    """LEB128 non signé : 7 bits/octet, bit7 = continuation (1-5 B)."""
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        out.append(b | 0x80 if n else b)
        if not n:
            return bytes(out)


def decode_varint(buf: bytes) -> tuple[int, int] | None:
    """→ (valeur, octets lus) ou None si préfixe incomplet.
    ValueError si overlong (>5 octets) ou valeur > u32."""
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


def send_frame(sock: socket.socket, cipher: RC4, payload: bytes) -> None:
    sock.sendall(encode_varint(len(payload)) + cipher.crypt(payload))


def recv_frame(sock: socket.socket, cipher: RC4,
               timeout: float = 5.0) -> bytes | None:
    sock.settimeout(timeout)
    try:
        # Préfixe varint lu octet par octet (≤5) jusqu'au groupe sans
        # bit de continuation ; EOF/timeout → None.
        vbuf = b''
        while True:
            vbuf += recv_exact(sock, 1)
            res = decode_varint(vbuf)
            if res is not None:
                dlen = res[0]
                break
        return cipher.crypt(recv_exact(sock, dlen))
    except (socket.timeout, ConnectionError):
        return None


def get_ticket(host: str, port: int, char_id: int,
               zone_ip: int, zone_port: int) -> bytes:
    """ServerGetTicket → 22B ticket [id][cip][sip][port][t1][t2]."""
    sock = socket.create_connection((host, port), timeout=10)
    sc, rc = handshake(sock)
    # [u32 id][u32 id2][u32 cip][u32 sip][u16 port]
    req = struct.pack('<IIIIH', char_id, char_id, 0x7f000001,
                      zone_ip, zone_port)
    send_frame(sock, sc, struct.pack('<H', 0) + req)
    resp = recv_frame(sock, rc)
    sock.close()
    if not resp or int.from_bytes(resp[0:2], 'little') != 2:
        raise RuntimeError('T_ GetTicket ko : ' + (resp or b'').hex())
    body = resp[2:]
    # resp = [id][id2][cip][sip][port][t1][t2] (26B)
    ticket = body[0:4] + body[8:26]     # [id][cip][sip][port][t1][t2]
    assert len(ticket) == 22, len(ticket)
    return ticket


def load_entry_frames(path: str) -> list[bytes]:
    """D frames de la dernière session de la capture — canal principal
    uniquement (le client ouvre une 2e connexion pour le canal
    auxiliaire : ses trames portent un autre ch= et ne sont pas
    rejouées sur la socket zone)."""
    frames: list[tuple[int, str, bytes]] = []
    for raw in open(path, errors='replace'):
        m = LINE.match(raw.strip())
        if not m or m.group(1) != 'D':
            continue
        frames.append((int(m.group(3)), m.group(2) or '',
                       bytes.fromhex(m.group(5))))
    last_ticket = max(i for i, (n, _, _) in enumerate(frames) if n == 0)
    main_ch = frames[last_ticket][1]
    return [p for n, ch, p in frames[last_ticket + 1:] if ch == main_ch]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--host', default='127.0.0.1')
    ap.add_argument('--zone-port', type=int, default=5201)
    ap.add_argument('--ticket-port', type=int, default=2067)
    ap.add_argument('--char-id', type=int, required=True)
    ap.add_argument('--capture', default=CAPTURE)
    ap.add_argument('--out')
    ap.add_argument('--listen', type=float, default=20.0,
                    help='secondes d\'écoute après l\'entrée')
    ap.add_argument('--wait', type=float, default=0.05)
    ap.add_argument('--max', type=int, default=0,
                    help='borne le nombre de D rejouées')
    ap.add_argument('--hold', type=float, default=0,
                    help='maintient la session N s après l\'écoute')
    ap.add_argument('--send', action='append', default=[],
                    metavar='NCID:HEX',
                    help='paquet brut à envoyer après le burst '
                         '(répétable, ex. --send 29:02000000)')
    ap.add_argument('--send-delay', type=float, default=0,
                    help='pause (s) entre la fin du burst et le '
                         'premier --send')
    args = ap.parse_args()

    ticket = get_ticket(args.host, args.ticket_port, args.char_id,
                        0x7f000001, args.zone_port)
    print(f'[session] ticket T_ OK char=#{args.char_id} '
          f'{ticket.hex()}')

    sock = socket.create_connection((args.host, args.zone_port),
                                    timeout=10)
    sc, rc = handshake(sock)
    print(f'[session] handshake zone OK')

    out = open(args.out, 'w') if args.out else None

    def log(tag: str, ncid: int, payload: bytes) -> None:
        if out:
            out.write(f'[{time.strftime("%H:%M:%S")}.'
                      f'{int(time.time() * 1000) % 1000:03d}] '
                      f'{tag} ch=headless NCID={ncid} '
                      f'len={len(payload)} {payload.hex()}\n')
            out.flush()

    # Listener arrière-plan : logge chaque trame serveur.
    stop = threading.Event()
    counts: dict[int, int] = {}

    def listener() -> None:
        while not stop.is_set():
            p = recv_frame(sock, rc, timeout=0.5)
            if p is None:
                continue
            ncid = int.from_bytes(p[0:2], 'little') if len(p) >= 2 else -1
            counts[ncid] = counts.get(ncid, 0) + 1
            log('E', ncid, p)
            if ncid == 265:
                print(f'[session] <<< NCID 265 ClientSeeChar '
                      f'({len(p)}B) {p[:12].hex()}…')
            if ncid == 269:
                print(f'[session] <<< NCID 269 LifeEradicate '
                      f'{p.hex()}')

    lt = threading.Thread(target=listener, daemon=True)
    lt.start()

    # NCID 0 = ticket frais (remplace le ticket capturé).
    send_frame(sock, sc, struct.pack('<H', 0) + ticket)
    log('D', 0, struct.pack('<H', 0) + ticket)

    sent = 1
    for payload in load_entry_frames(args.capture):
        send_frame(sock, sc, payload)
        log('D', int.from_bytes(payload[0:2], 'little'), payload)
        sent += 1
        time.sleep(args.wait)
        if args.max and sent >= args.max:
            break
    print(f'[session] {sent} trames envoyées (ticket inclus)')

    if args.send_delay:
        time.sleep(args.send_delay)
    for spec in args.send:
        ncid, _, hx = spec.partition(':')
        if ncid == 'sleep':
            time.sleep(float(hx))
            continue
        body = bytes.fromhex(hx)
        payload = struct.pack('<H', int(ncid)) + body
        send_frame(sock, sc, payload)
        log('D', int(ncid), payload)
        print(f'[session] >>> NCID {ncid} envoyé ({len(payload)}B)')
        time.sleep(args.wait)

    deadline = time.time() + args.listen
    while time.time() < deadline:
        time.sleep(0.5)
    print('[session] NCIDs reçus : ' + ', '.join(
        f'{n}×{c}' for n, c in sorted(counts.items())))

    if args.hold:
        print(f'[session] hold {args.hold}s…')
        time.sleep(args.hold)
    stop.set()
    sock.close()
    if out:
        out.close()
    return 0


if __name__ == '__main__':
    sys.exit(main())
