#!/usr/bin/env python3
# Démon uinput persistant — deux devices (clavier + pointeur) créés une
# fois et gardés ouverts ; commandes sur stdin, une par ligne :
#   key <nom> [duree]   click <x> <y> [right]   drag <x1> <y1> <x2> <y2>
#   type <texte>        quit
# AZERTY : 'w'=avant (scancode physique), comme sous X.
import sys, time
from evdev import UInput, AbsInfo, ecodes as E

KEYCODES = [getattr(E, n) for n in dir(E)
    if n.startswith('KEY_') and getattr(E, n) <= 0x180]
key = UInput({E.EV_KEY: KEYCODES}, name='eden-kb')
ptr = UInput({
    E.EV_KEY: [E.BTN_LEFT, E.BTN_RIGHT, E.BTN_MIDDLE],
    E.EV_REL: [E.REL_X, E.REL_Y, E.REL_WHEEL],
    E.EV_ABS: [(E.ABS_X, AbsInfo(0, 0, 3840, 0, 0, 0)),
               (E.ABS_Y, AbsInfo(0, 0, 1080, 0, 0, 0))]},
    name='eden-ptr')
time.sleep(1.0)

NAME = {'w': 'KEY_W', 'z': 'KEY_Z', 'q': 'KEY_Q', 's': 'KEY_S',
    'd': 'KEY_D', 'a': 'KEY_A', 'j': 'KEY_J', 'l': 'KEY_L',
    't': 'KEY_T', 'r': 'KEY_R', 'f': 'KEY_F', 'i': 'KEY_I',
    'm': 'KEY_M', 'c': 'KEY_C', 'k': 'KEY_K', 'x': 'KEY_X',
    'enter': 'KEY_ENTER', 'esc': 'KEY_ESC', 'space': 'KEY_SPACE',
    'tab': 'KEY_TAB'} | {str(i): f'KEY_{i}' for i in range(10)}
TYPEMAP = {c: getattr(E, 'KEY_' + c.upper()) for c in 'abcdefghijklmnopqrstuvwxyz'}
TYPEMAP.update({str(i): getattr(E, 'KEY_' + str(i)) for i in range(10)})
TYPEMAP.update({' ': E.KEY_SPACE, '/': E.KEY_SLASH, '_': E.KEY_MINUS,
    '-': E.KEY_MINUS, '.': E.KEY_DOT, ';': E.KEY_SEMICOLON})

for line in sys.stdin:
    p = line.split()
    if not p or p[0] == 'quit':
        break
    if p[0] == 'key':
        code = getattr(E, NAME[p[1]])
        dur = float(p[2]) if len(p) > 2 else 0.06
        key.write(E.EV_KEY, code, 1); key.syn(); time.sleep(dur)
        key.write(E.EV_KEY, code, 0); key.syn()
    elif p[0] == 'click':
        x, y = int(p[1]), int(p[2])
        btn = E.BTN_RIGHT if len(p) > 3 and p[3] == 'right' else E.BTN_LEFT
        ptr.write(E.EV_ABS, E.ABS_X, x); ptr.write(E.EV_ABS, E.ABS_Y, y)
        ptr.syn(); time.sleep(0.12)
        ptr.write(E.EV_KEY, btn, 1); ptr.syn(); time.sleep(0.05)
        ptr.write(E.EV_KEY, btn, 0); ptr.syn()
    elif p[0] == 'drag':
        x1, y1, x2, y2 = map(int, p[1:5])
        btn = E.BTN_RIGHT if len(p) > 5 and p[5] == 'right' else E.BTN_LEFT
        ptr.write(E.EV_ABS, E.ABS_X, x1); ptr.write(E.EV_ABS, E.ABS_Y, y1)
        ptr.syn(); time.sleep(0.12)
        ptr.write(E.EV_KEY, btn, 1); ptr.syn(); time.sleep(0.08)
        for i in range(1, 9):
            ptr.write(E.EV_ABS, E.ABS_X, x1 + (x2 - x1) * i // 8)
            ptr.write(E.EV_ABS, E.ABS_Y, y1 + (y2 - y1) * i // 8)
            ptr.syn(); time.sleep(0.02)
        time.sleep(0.08)
        ptr.write(E.EV_KEY, btn, 0); ptr.syn()
    elif p[0] == 'type':
        for c in ' '.join(p[1:]).lower():
            code = TYPEMAP.get(c)
            if code is None:
                continue
            key.write(E.EV_KEY, code, 1); key.syn(); time.sleep(0.03)
            key.write(E.EV_KEY, code, 0); key.syn(); time.sleep(0.03)
    sys.stdout.write('ok\n'); sys.stdout.flush()
key.close(); ptr.close()
