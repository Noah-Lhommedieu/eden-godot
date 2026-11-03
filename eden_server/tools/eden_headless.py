#!/usr/bin/env python3
"""Client headless Eden — handshake RSA+RC4 puis rejeu de trames.

Reproduit la séquence de connexion du client officiel :
    1. réception de la clé publique RSA-2048 :
       [u32 n_len][u32 e_len][n (BE)][e (BE)]
    2. envoi de 256 octets = RSA_public_encrypt(clé RC4 5 octets,
       padding PKCS#1 v1.5 type 2)
    3. trames [varint len en clair][RC4(u16 NCID + champs)]

Modes :
    --replay <capture.bin> : envoie, dans l'ordre, les paquets
        D (client→serveur) de la trace zsniff — bodies déjà en clair,
        rechiffrés avec la clé RC4 de session.
    --listen N             : attend N trames serveur après chaque envoi
        (défaut : drain non-bloquant).

Sortie : trace au format zsniff sur --out (rejouable par wire_diff.py
contre la capture de référence ou une trace native).

Exemple :
    tools/eden_headless.py --port 5201 \\
        --replay captures/zsniff_cs_entry_success.bin \\
        --out /tmp/cs_replay.log
"""
import argparse
import json
import os
import re
import socket
import sys
import time

LINE = re.compile(
    r'^\[[\d:]+\.\d+\]\s+([EDS])\s+(?:ch=[0-9a-f]+\s+)?'
    r'NCID=(\d+)\s+len=(\d+)\s+([0-9a-fA-F]+)\s*$')


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
    """PKCS#1 v1.5 block type 2 — ce qu'envoie le client (256B)."""
    k = (n.bit_length() + 7) // 8
    ps_len = k - len(key) - 3
    ps = bytearray()
    while len(ps) < ps_len:
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
    frame = encode_varint(len(payload)) + cipher.crypt(payload)
    sock.sendall(frame)


def drain(sock: socket.socket, cipher: RC4, out, timeout: float,
          log_prefix: str = '') -> int:
    """Lit les trames serveur disponibles jusqu'à `timeout` s de silence."""
    sock.settimeout(timeout)
    buf = b''
    n_read = 0
    while True:
        try:
            chunk = sock.recv(65536)
            if not chunk:
                break
        except socket.timeout:
            break
        buf += chunk
        while buf:
            res = decode_varint(buf)
            if res is None:
                break               # préfixe varint incomplet
            dlen, hdr = res
            if len(buf) < hdr + dlen:
                break               # payload incomplet
            payload = cipher.crypt(buf[hdr:hdr + dlen])
            buf = buf[hdr + dlen:]
            ncid = payload[0] | (payload[1] << 8) if len(payload) >= 2 else -1
            n_read += 1
            if out:
                out.write(f'{log_prefix}'
                          f'[{time.strftime("%H:%M:%S")}.'
                          f'{int(time.time() * 1000) % 1000:03d}] '
                          f'E ch=headless NCID={ncid} len={len(payload)} '
                          f'{payload.hex()}\n')
    return n_read


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--host', default='127.0.0.1')
    ap.add_argument('--port', type=int, required=True)
    ap.add_argument('--replay', help='capture zsniff dont on rejoue les D')
    ap.add_argument('--out', help='fichier de trace zsniff des réponses')
    ap.add_argument('--listen', type=float, default=1.5,
                    help='secondes de drain après le rejeu (défaut 1.5)')
    ap.add_argument('--wait', type=float, default=0.05,
                    help='pause entre trames rejouées (défaut 50ms)')
    ap.add_argument('--max', type=int, default=0,
                    help='borne le nombre de trames rejouées')
    args = ap.parse_args()

    sock = socket.create_connection((args.host, args.port), timeout=10)
    send_cipher, recv_cipher = handshake(sock)
    print(f'[headless] handshake OK {args.host}:{args.port}')

    out = open(args.out, 'w') if args.out else None
    sent = 0
    if args.replay:
        for raw in open(args.replay, errors='replace'):
            m = LINE.match(raw.strip())
            if not m or m.group(1) != 'D':
                continue
            payload = bytes.fromhex(m.group(4))
            send_frame(sock, send_cipher, payload)
            sent += 1
            if out:
                out.write(f'[{time.strftime("%H:%M:%S")}.'
                          f'{int(time.time() * 1000) % 1000:03d}] '
                          f'D ch=headless NCID={int(m.group(2))} '
                          f'len={len(payload)} {payload.hex()}\n')
            if args.wait:
                time.sleep(args.wait)
            if args.max and sent >= args.max:
                break
        print(f'[headless] {sent} trames rejouées')

    got = drain(sock, recv_cipher, out, args.listen)
    print(f'[headless] {got} trames serveur lues')
    if out:
        out.close()
        print(f'[headless] trace → {args.out}')
    sock.close()
    return 0


if __name__ == '__main__':
    sys.exit(main())
