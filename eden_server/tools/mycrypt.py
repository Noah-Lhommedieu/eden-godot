#!/usr/bin/env python3
"""
MyCrypt decoder for Eden Eternal server .ini files.

Algorithm: PKZIP/ZipCrypto stream cipher (Biham-Kocher target).
Reversed from server_us/ZoneServer/ZoneServer (x86 ELF):

  MyCrypt::init_keys      @ 0x08594d70  -> keys = {0x12345678, 0x23456789, 0x34567890}
  MyCrypt::update_keys    @ 0x08594be0  -> k0=CRC32(k0,b); k1=(k1+(k0&0xFF))*0x8088405+1; k2=CRC32(k2,k1>>24)
  MyCrypt::decrypt_byte   @ 0x08594bc0  -> t=(k2&0xFFFF)|2; return (t*(t^1))>>8 & 0xFF
  MyCrypt::crypt_decode   @ 0x08595050  -> init_keys(password); for each byte: plain=cipher^decrypt_byte; update_keys(plain)
  MyCrypt::crypthead      @ 0x085952e0  -> encode-side random header generator (rand()>>7)
  MyCrypt::coding         @ 0x08595b90  -> dispatcher (modes 1/2/3)

PASSWORD (hardcoded, found at .rodata 0x87cdff0, passed by both callers
main @ 0x80dd1bc and GameData::ReadINIFile @ 0x85b9453):  "ghfdsjdl"

File format: <encode> (8 bytes magic) + raw ciphertext (no IV, no random
header in stored files — plaintext starts at ciphertext byte 0).

Usage:
  python3 mycrypt.py decode <input.ini> [output.ini]
  python3 mycrypt.py decode-all <db_dir> <out_dir>
"""

import sys, os

PASSWORD = b'ghfdsjdl'

def _build_crc32_table():
    table = []
    for i in range(256):
        c = i
        for _ in range(8):
            c = (c >> 1) ^ 0xEDB88320 if (c & 1) else (c >> 1)
        table.append(c)
    return table

CRC32 = _build_crc32_table()

def _update_keys(k, b):
    k[0] = (k[0] >> 8) ^ CRC32[(k[0] ^ b) & 0xFF]
    k[1] = ((k[1] + (k[0] & 0xFF)) * 0x8088405 + 1) & 0xFFFFFFFF
    k[2] = (k[2] >> 8) ^ CRC32[(k[2] ^ (k[1] >> 24)) & 0xFF]

def _decrypt_byte(k):
    t = (k[2] & 0xFFFF) | 2
    return ((t * (t ^ 1)) >> 8) & 0xFF

def crypt_decode(data: bytes, password: bytes = PASSWORD) -> bytes:
    k = [0x12345678, 0x23456789, 0x34567890]
    for c in password:
        _update_keys(k, c)
    out = bytearray(len(data))
    for i, cb in enumerate(data):
        out[i] = cb ^ _decrypt_byte(k)
        _update_keys(k, out[i])
    return bytes(out)

def decode_file(path: str) -> bytes:
    raw = open(path, 'rb').read()
    if raw[:8] == b'<encode>':
        return crypt_decode(raw[8:])
    return raw

def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    cmd = sys.argv[1]
    if cmd == 'decode':
        src = sys.argv[2]
        dst = sys.argv[3] if len(sys.argv) > 3 else src + '.decoded'
        open(dst, 'wb').write(decode_file(src))
        print(f"{src} -> {dst}")
    elif cmd == 'decode-all':
        src_dir, dst_dir = sys.argv[2], sys.argv[3]
        os.makedirs(dst_dir, exist_ok=True)
        n = 0
        for f in sorted(os.listdir(src_dir)):
            if not f.endswith('.ini'):
                continue
            open(os.path.join(dst_dir, f), 'wb').write(
                decode_file(os.path.join(src_dir, f)))
            n += 1
        print(f"decoded {n} files -> {dst_dir}")

if __name__ == '__main__':
    main()
