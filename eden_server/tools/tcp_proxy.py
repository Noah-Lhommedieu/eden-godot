#!/usr/bin/env python3
"""Proxy TCP hex-dump — capture le wire format natif client<->serveur."""
import socket, threading, sys, time, select

LISTEN = ('127.0.0.1', int(sys.argv[1]) if len(sys.argv) > 1 else 6543)
TARGET = ('127.0.0.1', int(sys.argv[2]) if len(sys.argv) > 2 else 6544)
LOG = sys.argv[3] if len(sys.argv) > 3 else '/tmp/proxy-capture.log'
lock = threading.Lock()

def dump(tag, cid, data):
    with lock, open(LOG, 'a') as f:
        f.write(f"[{time.strftime('%H:%M:%S')}.{int(time.time()*1000)%1000:03d}] {tag} conn#{cid} len={len(data)}\n")
        f.write('  ' + ' '.join(f'{b:02x}' for b in data) + '\n')
        f.write('  ascii: ' + ''.join(chr(b) if 32 <= b < 127 else '.' for b in data) + '\n')

def pipe(src, dst, tag, cid):
    try:
        while True:
            d = src.recv(65536)
            if not d:
                break
            dump(tag, cid, d)
            dst.sendall(d)
    except OSError:
        pass
    try:
        dst.shutdown(socket.SHUT_WR)
    except OSError:
        pass

cid_counter = 0
srv = socket.socket()
srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(LISTEN)
srv.listen(16)
print(f"proxy {LISTEN} -> {TARGET}, log={LOG}")
while True:
    c, addr = srv.accept()
    cid_counter += 1
    cid = cid_counter
    t = socket.socket()
    try:
        t.connect(TARGET)
    except OSError as e:
        dump('TARGET-FAIL', cid, str(e).encode())
        c.close()
        continue
    threading.Thread(target=pipe, args=(c, t, 'C->S', cid), daemon=True).start()
    threading.Thread(target=pipe, args=(t, c, 'S->C', cid), daemon=True).start()
