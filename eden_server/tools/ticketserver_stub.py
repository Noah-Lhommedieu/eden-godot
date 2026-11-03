#!/usr/bin/env python3
"""Stub TicketServer natif Eden (port 2067, framing [varint len][payload], pas de crypto).
Table InitTicketCommandTable:
  0 CNC_T_ServerGetTicket  {u32,u32,u32,u32}
  1 CNC_T_ServerUseTicket  {u32 key, ticket[22]}
  2 CNC_T_ClientGetTicket  {u32 key, ticket[22]}
  3 CNC_T_ClientUseTicket  {u32 key, u8 result !=0 => OK}
  4 CNC_T_ServerCheckPin
  5 CNC_T_ClientCheckPin
"""
import socket,struct,threading,os,time,random

PORT=2067
LOG='/tmp/eden-ticketstub.log'
def log(m):
    open(LOG,'a').write(f"{time.strftime('%H:%M:%S')} {m}\n"); print(m,flush=True)

def encode_varint(n):
    """LEB128 non signé : 7 bits/octet, bit7 = continuation (1-5 B)."""
    out=bytearray()
    while True:
        b=n&0x7F; n>>=7
        out.append(b|0x80 if n else b)
        if not n: return bytes(out)

def decode_varint(buf):
    """-> (valeur, octets lus) ou None si préfixe incomplet.
    ValueError si overlong (>5 octets) ou valeur > u32."""
    val=0
    for i,b in enumerate(buf[:5]):
        if i==4 and b&0x70: raise ValueError('varint > u32')
        val|=(b&0x7F)<<(7*i)
        if not b&0x80: return val,i+1
    if len(buf)>=5: raise ValueError('varint overlong')
    return None

def send_frame(c,ncid,body):
    payload=struct.pack('<H',ncid)+body
    c.sendall(encode_varint(len(payload))+payload)
    log(f"  -> NCID={ncid} {payload.hex()}")

def handle(c,addr):
    log(f"[TS] connexion {addr}")
    buf=b''
    try:
        while True:
            data=c.recv(4096)
            if not data: break
            buf+=data
            while buf:
                res=decode_varint(buf)
                if res is None: break            # préfixe incomplet
                ln,hdr=res
                if len(buf)<hdr+ln: break        # payload incomplet
                payload=buf[hdr:hdr+ln]; buf=buf[hdr+ln:]
                ncid=struct.unpack('<H',payload[:2])[0]
                body=payload[2:]
                log(f"  <- NCID={ncid} len={ln} body={body.hex()}")
                if ncid==0:   # ServerGetTicket {u32 k,u32,u32,u32} -> ClientGetTicket
                    key=struct.unpack('<I',body[:4])[0]
                    ticket=struct.pack('<I',key)+struct.pack('<I',0x7f000001)+struct.pack('<I',0x7f000001)+struct.pack('<H',5710)+struct.pack('<I',int(time.time()*1000)&0xffffffff)+struct.pack('<I',random.getrandbits(32))
                    send_frame(c,2,key.to_bytes(4,'little')+ticket)   # key + ticket[22]
                elif ncid==1: # ServerUseTicket {u32 key, ticket[22]} -> ClientUseTicket
                    key=struct.unpack('<I',body[:4])[0]
                    send_frame(c,3,struct.pack('<I',key)+b'\x01')  # result!=0 => OK
                elif ncid==4: # ServerCheckPin -> ClientCheckPin
                    send_frame(c,5,body[:4]+b'\x01')
    except Exception as e:
        log(f"[TS] err {e}")
    log(f"[TS] fin {addr}")

s=socket.socket(); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1)
s.bind(('0.0.0.0',PORT)); s.listen(8)
log(f"[TS] ecoute :{PORT}")
while True:
    c,a=s.accept(); threading.Thread(target=handle,args=(c,a),daemon=True).start()
