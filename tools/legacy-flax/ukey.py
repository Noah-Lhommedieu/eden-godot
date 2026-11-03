#!/usr/bin/env python3
# Injection kernel-level via evdev/UInput — marche sous Wayland.
# ukey.py key z [dur] | click x y [right] | move dx dy | drag x1 y1 x2 y2
import sys, time
from evdev import UInput, AbsInfo, ecodes as E

KEYCODES = list(set([E.KEY_W, E.KEY_Z, E.KEY_Q, E.KEY_S, E.KEY_D,
    E.KEY_A, E.KEY_J, E.KEY_L, E.KEY_T, E.KEY_R, E.KEY_F, E.KEY_I,
    E.KEY_M, E.KEY_C, E.KEY_K, E.KEY_X, E.KEY_ENTER, E.KEY_ESC,
    E.KEY_LEFTCTRL, E.KEY_B, E.KEY_E, E.KEY_G, E.KEY_H, E.KEY_N,
    E.KEY_O, E.KEY_P, E.KEY_U, E.KEY_V, E.KEY_Y, E.KEY_SLASH,
    E.KEY_MINUS, E.KEY_DOT, E.KEY_SEMICOLON, E.KEY_APOSTROPHE,
    E.KEY_1, E.KEY_2, E.KEY_3, E.KEY_4, E.KEY_5, E.KEY_6, E.KEY_7,
    E.KEY_8, E.KEY_9, E.KEY_0, E.KEY_SPACE, E.KEY_TAB]))
PTR_BTNS = [E.BTN_LEFT, E.BTN_RIGHT, E.BTN_MIDDLE]
mode = sys.argv[1]
if mode == 'key':
    cap = {E.EV_KEY: KEYCODES + [E.KEY_RESERVED],
        E.EV_LED: [E.LED_NUML, E.LED_CAPSL]}
else:
    cap = {E.EV_KEY: PTR_BTNS,
        E.EV_REL: [E.REL_X, E.REL_Y, E.REL_WHEEL],
        E.EV_ABS: [(E.ABS_X, AbsInfo(0, 0, 3840, 0, 0, 0)),
                   (E.ABS_Y, AbsInfo(0, 0, 1080, 0, 0, 0))]}
TYPEMAP = {}
for c, k in {'w':'W','z':'Z','q':'Q','s':'S','d':'D','a':'A','j':'J','l':'L','t':'T','r':'R','f':'F','i':'I','m':'M','c':'C','k':'K','x':'X','b':'B','e':'E','g':'G','h':'H','n':'N','o':'O','p':'P','u':'U','v':'V','y':'Y'}.items():
    TYPEMAP[c] = getattr(E, 'KEY_' + k)
TYPEMAP.update({' ': E.KEY_SPACE, '/': E.KEY_SLASH, '_': E.KEY_MINUS,
    '-': E.KEY_MINUS, '.': E.KEY_DOT, ';': E.KEY_SEMICOLON,
    "'": E.KEY_APOSTROPHE})
TYPEMAP.update({str(i): getattr(E, 'KEY_' + str(i)) for i in range(10)})
NAME = {'w': 'KEY_W', 'z': 'KEY_Z', 'q': 'KEY_Q', 's': 'KEY_S',
    'd': 'KEY_D', 'a': 'KEY_A', 'j': 'KEY_J', 'l': 'KEY_L',
    't': 'KEY_T', 'r': 'KEY_R', 'f': 'KEY_F', 'i': 'KEY_I',
    'm': 'KEY_M', 'c': 'KEY_C', 'k': 'KEY_K', 'x': 'KEY_X',
    'enter': 'KEY_ENTER', 'esc': 'KEY_ESC', 'space': 'KEY_SPACE',
    'tab': 'KEY_TAB'} | {str(i): f'KEY_{i}' for i in range(10)}

ui = UInput(cap, name='eden-uinput')
time.sleep(0.4)
if mode == 'key':
    code = getattr(E, NAME[sys.argv[2]])
    dur = float(sys.argv[3]) if len(sys.argv) > 3 else 0.06
    ui.write(E.EV_KEY, code, 1); ui.syn(); time.sleep(dur)
    ui.write(E.EV_KEY, code, 0); ui.syn()
elif mode == 'click':
    x, y = int(sys.argv[2]), int(sys.argv[3])
    btn = E.BTN_RIGHT if len(sys.argv) > 4 and sys.argv[4] == 'right' else E.BTN_LEFT
    ui.write(E.EV_ABS, E.ABS_X, x); ui.write(E.EV_ABS, E.ABS_Y, y)
    ui.syn(); time.sleep(0.15)
    ui.write(E.EV_KEY, btn, 1); ui.syn(); time.sleep(0.05)
    ui.write(E.EV_KEY, btn, 0); ui.syn()
elif mode == 'absdrag':
    x1, y1, x2, y2 = map(int, sys.argv[2:6])
    btn = E.BTN_RIGHT if len(sys.argv) > 6 and sys.argv[6] == 'right' else E.BTN_LEFT
    ui.write(E.EV_ABS, E.ABS_X, x1); ui.write(E.EV_ABS, E.ABS_Y, y1)
    ui.syn(); time.sleep(0.15)
    ui.write(E.EV_KEY, btn, 1); ui.syn(); time.sleep(0.1)
    steps = 8
    for i in range(1, steps + 1):
        ui.write(E.EV_ABS, E.ABS_X, x1 + (x2 - x1) * i // steps)
        ui.write(E.EV_ABS, E.ABS_Y, y1 + (y2 - y1) * i // steps)
        ui.syn(); time.sleep(0.02)
    time.sleep(0.1)
    ui.write(E.EV_KEY, btn, 0); ui.syn()
elif mode == 'move':
    ui.write(E.EV_REL, E.REL_X, int(sys.argv[2]))
    ui.write(E.EV_REL, E.REL_Y, int(sys.argv[3])); ui.syn()
elif mode == 'drag':
    x1, y1, x2, y2 = map(int, sys.argv[2:6])
    ui.write(E.EV_REL, E.REL_X, -6000); ui.write(E.EV_REL, E.REL_Y, -6000); ui.syn()
    ui.write(E.EV_REL, E.REL_X, x1); ui.write(E.EV_REL, E.REL_Y, y1); ui.syn()
    time.sleep(0.15)
    ui.write(E.EV_KEY, E.BTN_RIGHT, 1); ui.syn(); time.sleep(0.1)
    ui.write(E.EV_REL, E.REL_X, x2 - x1); ui.write(E.EV_REL, E.REL_Y, y2 - y1)
    ui.syn(); time.sleep(0.15)
    ui.write(E.EV_KEY, E.BTN_RIGHT, 0); ui.syn()
ui.close()
