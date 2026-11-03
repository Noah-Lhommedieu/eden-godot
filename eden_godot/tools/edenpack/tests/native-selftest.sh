#!/usr/bin/env bash
# native-selftest.sh — preuve du mode natif d'edenpack sur un mini corpus
# SYNTHÉTIQUE (pas de données de jeu, clés de TEST générées à la volée).
#
#   tools/edenpack/tests/native-selftest.sh [workdir]
#
# Couvre : build --native (4 paquets), routage, DDS BC raw, P2 (échec sur
# CRC faux / fichier hors manifeste, tolérance des exceptions connues),
# verify + --tamper-test, falsification d'un octet refusée, P5
# diff -> apply (re-signé) -> install v2 -> rollback v1, install qui
# refuse un paquet corrompu et laisse current.json intact.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$HERE/.."
W="${1:-${TMPDIR:-/tmp}/edenpack-selftest}"
rm -rf "$W"; mkdir -p "$W"
EP=(dotnet "$W/bin/edenpack.dll")
fail() { echo "ECHEC: $*" >&2; exit 1; }
expect_fail() { # <msg-regex> <cmd...>
    local rx="$1"; shift
    if out="$("$@" 2>&1)"; then fail "devait échouer : $*"; fi
    echo "$out" | grep -Eq "$rx" || fail "message attendu /$rx/ : $out"
    echo "ok (refus attendu) : $rx"
}

dotnet build "$PROJ/edenpack.csproj" -c Release -o "$W/bin" -v q >/dev/null

# --- mini corpus + manifeste officiel synthétique ---------------------
python3 - "$W" <<'PY'
import os, sys, struct
W = sys.argv[1]
def crc32c(b):
    t = []
    for i in range(256):
        c = i
        for _ in range(8): c = (c >> 1) ^ 0x82F63B78 if c & 1 else c >> 1
        t.append(c)
    c = 0xFFFFFFFF
    for x in b: c = t[(c ^ x) & 0xFF] ^ (c >> 8)
    return c ^ 0xFFFFFFFF
def dds(fourcc, n):
    h = bytearray(128); h[0:4] = b'DDS '
    if fourcc: struct.pack_into('<I', h, 80, 4); h[84:88] = fourcc
    return bytes(h) + (os.urandom(n) if fourcc else b'\x07' * n)
files = {
 'uidata/layouts/Login.layout': b'<layout login/>' * 50,
 'uidata/layouts/other.layout': b'<layout other/>' * 50,
 'uidata/schemes/x.scheme': b'<scheme/>' * 20,
 'data/db/t_a.ini': b'a=1\n' * 500,
 'data/db/t_extra.ini': b'extra=1\n' * 100,        # hors manifeste (pkg)
 'map/model/S001/S001.fsm': b'FSM' * 300,
 'map/model/S001/S001.nif': b'NIF' * 3000,
 'map/model/S001/S001.fsmp': b'x' * 10,            # non empaqueté
 'map/texture/m.dds': dds(b'DXT5', 4000),
 'char/model/a.nif': bytes(range(256)) * 20,
 'char/texture/bc.dds': dds(b'DXT1', 5000),
 'char/texture/plain.dds': dds(None, 5000),
 'sound/s.wav': b'RIFF' + b'\x01' * 500,           # exception connue
 'bgm/b.ogg': os.urandom(800),
 'Launcher.exe': b'MZ' * 10,                       # non empaqueté
}
man = []
for p, d in files.items():
    c = crc32c(d)
    if p == 'sound/s.wav': c ^= 0xFF                # manifeste != installé
    if p == 'data/db/t_extra.ini': continue
    man.append('%s:20101106055246:%d:%d\r\n' % (p.replace('/', '\\'), len(d), c))
os.makedirs(W + '/corpus', exist_ok=True)
for p, d in files.items():
    f = os.path.join(W, 'corpus', p); os.makedirs(os.path.dirname(f), exist_ok=True)
    open(f, 'wb').write(d)
open(W + '/manifest.txt', 'w', encoding='latin-1', newline='').write(''.join(man))
open(W + '/verify.tsv', 'w').write('sound/s.wav\tAUCUNE-SOURCE\tCRC@0\n')
open(W + '/extract.tsv', 'w').write('data/db/t_extra.ini\tOK-pkg-HORS-MANIFESTE\t\n')
g = W + '/gamedata'
os.makedirs(g + '/data/db/lang/fr', exist_ok=True)
open(g + '/client.cfg', 'w').write('host=127.0.0.1\n')
open(g + '/settings.cfg', 'w').write('x=1\n')
open(g + '/levels.tsv', 'w').write('1\t100\n' * 50)
open(g + '/login_bg.jpg', 'wb').write(b'\xff\xd8' + os.urandom(300))
open(g + '/login_bg.jpg.import', 'w').write('[remap]\n')
open(g + '/data/db/lang/fr/t_a.ini', 'w').write('fr=1\n' * 80)
PY

"${EP[@]}" keygen "$W/keys" >/dev/null
K="$W/keys"
COMMON=(--corpus "$W/corpus" --gamedata "$W/gamedata"
        --manifest "$W/manifest.txt" --verify-tsv "$W/verify.tsv"
        --extract-tsv "$W/extract.tsv" --sign-key "$K/sign.key"
        --keyfile "$K/keyfile.txt")

echo "== build v1"
"${EP[@]}" build --native "$W/v1" "${COMMON[@]}" --version 1 --level 3 \
    >"$W/v1.log" 2>&1 || { cat "$W/v1.log"; fail "build v1"; }
cat "$W/v1.log" | grep -E "AVERTISSEMENT|provenance|OK|EXCEPTION|HORS|OVERLAY" || true
for p in bootstrap eden-core eden-assets eden-audio; do
    [ -f "$W/v1/$p.epk" ] || fail "$p.epk absent"
done

echo "== routage"
lst() { "${EP[@]}" list "$W/v1/$1.epk" 2>/dev/null; }
lst bootstrap | grep -qx 'uidata/layouts/login.layout' || fail "login hors bootstrap"
lst bootstrap | grep -qx 'login_bg.jpg' || fail "login_bg hors bootstrap"
lst eden-core | grep -qx 'uidata/layouts/other.layout' || fail "layout other hors core"
lst eden-core | grep -qx 'map/model/s001/s001.fsm' || fail "fsm hors core"
lst eden-core | grep -qx 'data/db/lang/fr/t_a.ini' || fail "overlay hors core"
lst eden-assets | grep -qx 'map/model/s001/s001.nif' || fail "map nif hors assets"
lst eden-audio | grep -qx 'sound/s.wav' || fail "sound hors audio"
{ lst bootstrap; lst eden-core; lst eden-assets; lst eden-audio; } \
    | grep -Eq 'fsmp|launcher|settings.cfg|\.import' && fail "fichier à exclure empaqueté"
echo ok

echo "== codec : DDS BC raw, DDS non compressé zstd"
INFO="$("${EP[@]}" info "$W/v1/eden-assets.epk")"
echo "$INFO" | grep -Eq 'Raw +char/texture/bc.dds' || fail "bc.dds doit être Raw"
echo "$INFO" | grep -Eq 'Raw +map/texture/m.dds' || fail "m.dds doit être Raw"
echo "$INFO" | grep -Eq 'Zstd +char/texture/plain.dds' || fail "plain.dds doit être Zstd"
echo "$INFO" | grep -Eq 'Zstd +char/model/a.nif' || fail "nif doit être Zstd"
echo ok

echo "== provenance.tsv"
grep -q $'\tOK$' "$W/v1/provenance.tsv" || fail "aucune ligne OK"
grep -q "EXCEPTION-AUCUNE-SOURCE" "$W/v1/provenance.tsv" || fail "exception sound absente"
grep -q "PKG-HORS-MANIFESTE" "$W/v1/provenance.tsv" || fail "hors-manifeste absent"
grep -q "OVERLAY-PROJET" "$W/v1/provenance.tsv" || fail "overlay absent"

echo "== verify (signature + déchiffrement + sha256) et tamper-test"
for p in bootstrap eden-core eden-assets eden-audio; do
    "${EP[@]}" verify "$W/v1/$p.epk" --pubkey "$K/sign.pub" \
        --keyfile "$K/keyfile.txt" --tamper-test | tee "$W/verify-$p.log" \
        | tail -3
    grep -q "NON DÉTECTÉ" "$W/verify-$p.log" && fail "verify $p"
done
"${EP[@]}" info "$W/v1/bootstrap.epk" | grep -qi encrypted && fail "bootstrap doit être en clair" || true

echo "== falsification d'un octet -> refus"
cp "$W/v1/eden-core.epk" "$W/bad.epk"
python3 - "$W/bad.epk" <<'PY'
import sys
f = open(sys.argv[1], 'r+b'); f.seek(200); b = f.read(1); f.seek(200); f.write(bytes([b[0] ^ 1])); f.close()
PY
expect_fail "ÉCHEC|erreur" "${EP[@]}" verify "$W/bad.epk" --pubkey "$K/sign.pub" --keyfile "$K/keyfile.txt"

echo "== P2 : le build échoue sur une entrée non conforme"
cp -r "$W/corpus" "$W/corpus-bad"
printf 'Z' | dd of="$W/corpus-bad/char/model/a.nif" bs=1 seek=7 conv=notrunc 2>/dev/null
expect_fail "PROVENANCE.*char/model/a.nif" "${EP[@]}" build --native "$W/bad1" \
    "${COMMON[@]}" --corpus "$W/corpus-bad" --level 1
cp -r "$W/corpus" "$W/corpus-bad2"
mkdir -p "$W/corpus-bad2/item"; echo x > "$W/corpus-bad2/item/unknown.nif"
expect_fail "PROVENANCE.*item/unknown.nif" "${EP[@]}" build --native "$W/bad2" \
    "${COMMON[@]}" --corpus "$W/corpus-bad2" --level 1
# une dérive CRC hors liste d'exceptions échoue, même sous sound/
: > "$W/verify-empty.tsv"
expect_fail "PROVENANCE.*sound/s.wav" "${EP[@]}" build --native "$W/bad3" \
    "${COMMON[@]}" --verify-tsv "$W/verify-empty.tsv" --level 1
[ ! -e "$W/bad1/eden-assets.epk.building" ] || fail ".building résiduel"
expect_fail "sign-key" "${EP[@]}" build --native "$W/bad4" --corpus "$W/corpus" \
    --gamedata "$W/gamedata" --manifest "$W/manifest.txt" \
    --verify-tsv "$W/verify.tsv" --extract-tsv "$W/extract.tsv"

echo "== reproductibilité : même entrée -> même octets"
"${EP[@]}" build --native "$W/v1b" "${COMMON[@]}" --version 1 --level 3 \
    >/dev/null 2>&1
# seuls le bloc wrap (nonce aléatoire) et la signature ECDSA peuvent différer
for p in bootstrap eden-core eden-assets eden-audio; do
    python3 - "$W/v1/$p.epk" "$W/v1b/$p.epk" <<'PY' || fail "$p non déterministe"
import sys
a, b = (open(f, 'rb').read() for f in sys.argv[1:])
enc = int.from_bytes(a[8:12], 'little') & 1
lo = 80; hi = lo + (60 if enc else 0) + 68   # wrap (nonce aléatoire) + signature
assert len(a) == len(b), 'taille'
bad = [i for i in range(len(a)) if a[i] != b[i] and not lo <= i < hi]
assert not bad, 'octets différents hors signature : %s' % bad[:5]
PY
done
echo ok

echo "== v2 : fichier modifié + supprimé + ajouté"
echo "1	999" > "$W/gamedata/levels.tsv"                   # modifié (core)
rm "$W/corpus/char/texture/plain.dds"                       # supprimé (assets)
mkdir -p "$W/corpus/ride"; printf 'RIDE%.0s' {1..200} > "$W/corpus/ride/r.nif"
python3 - "$W" <<'PY'
import sys
W = sys.argv[1]
t = []
for i in range(256):
    c = i
    for _ in range(8): c = (c >> 1) ^ 0x82F63B78 if c & 1 else c >> 1
    t.append(c)
d = open(W + '/corpus/ride/r.nif', 'rb').read()
c = 0xFFFFFFFF
for x in d: c = t[(c ^ x) & 0xFF] ^ (c >> 8)
open(W + '/manifest.txt', 'a', newline='').write('ride\\r.nif:20101106055246:%d:%d\r\n' % (len(d), c ^ 0xFFFFFFFF))
PY
"${EP[@]}" build --native "$W/v2" "${COMMON[@]}" --version 2 --level 3 \
    --prev-dir "$W/v1" >/dev/null 2>&1 || fail "build v2"

echo "== P5 diff -> apply (re-signé) -> install -> rollback"
mkdir -p "$W/patch" "$W/applied"
for p in bootstrap eden-core eden-assets eden-audio; do
    "${EP[@]}" diff "$W/v1/$p.epk" "$W/v2/$p.epk" "$W/patch/$p.epk" | tail -1
    "${EP[@]}" apply "$W/v1/$p.epk" "$W/patch/$p.epk" "$W/applied/$p.epk" \
        --sign-key "$K/sign.key" >/dev/null
    "${EP[@]}" verify "$W/applied/$p.epk" --pubkey "$K/sign.pub" \
        --keyfile "$K/keyfile.txt" | grep -q "signature OK" \
        || fail "apply $p non signé"
    diff <("${EP[@]}" list "$W/applied/$p.epk" 2>/dev/null) \
         <("${EP[@]}" list "$W/v2/$p.epk" 2>/dev/null) \
        || fail "apply $p : liste != v2"
done
mkdir -p "$W/x-applied" "$W/x-v2"
"${EP[@]}" extract "$W/applied/eden-assets.epk" "$W/x-applied" --keyfile "$K/keyfile.txt" >/dev/null
"${EP[@]}" extract "$W/v2/eden-assets.epk" "$W/x-v2" --keyfile "$K/keyfile.txt" >/dev/null
diff -r "$W/x-applied" "$W/x-v2" || fail "apply != build v2 (assets)"
[ ! -e "$W/x-applied/char/texture/plain.dds" ] || fail "suppression non appliquée"
[ -f "$W/x-applied/ride/r.nif" ] || fail "ajout non appliqué"

GD="$W/install"
"${EP[@]}" install "$W/v1" "$GD" --sign-key "$K/sign.key" | tail -1
cp "$GD/current.json" "$W/current-v1.json"
"${EP[@]}" install "$W/applied" "$GD" --sign-key "$K/sign.key" | tail -1
grep -q '"version": *2' "$GD/current.json" || grep -q '"version":2' "$GD/current.json" || fail "bascule v2"
cmp "$GD/eden-assets.epk" "$W/applied/eden-assets.epk" || fail "v2 non installé"
echo "-- rollback : réinstalle la release v1"
"${EP[@]}" install "$W/v1" "$GD" --sign-key "$K/sign.key" | tail -1
cmp "$GD/eden-assets.epk" "$W/v1/eden-assets.epk" || fail "rollback assets"
cmp "$GD/eden-core.epk" "$W/v1/eden-core.epk" || fail "rollback core"
grep -q '"version": *1' "$GD/current.json" || grep -q '"version":1' "$GD/current.json" || fail "rollback pointeur"

echo "-- install d'un set corrompu : refus, pointeur intact"
mkdir -p "$W/corrupt"; cp "$W/applied/"*.epk "$W/corrupt/"
head -c 100 "$W/corrupt/eden-core.epk" > "$W/corrupt/tmp" && mv "$W/corrupt/tmp" "$W/corrupt/eden-core.epk"
before="$(sha256sum "$GD/current.json" | cut -d' ' -f1)"
expect_fail "." "${EP[@]}" install "$W/corrupt" "$GD" --sign-key "$K/sign.key"
after="$(sha256sum "$GD/current.json" | cut -d' ' -f1)"
[ "$before" = "$after" ] || fail "current.json modifié par un install raté"
cmp "$GD/eden-core.epk" "$W/v1/eden-core.epk" || fail "pack actif modifié"

echo "-- install d'un pack en clair falsifié (blob) : refus, pointeur intact"
mkdir -p "$W/corrupt2"; cp "$W/applied/"*.epk "$W/corrupt2/"
python3 - "$W/corrupt2/bootstrap.epk" <<'PY'
import sys
f = open(sys.argv[1], 'r+b'); f.seek(160); b = f.read(1); f.seek(160); f.write(bytes([b[0] ^ 1])); f.close()
PY
expect_fail "sha256 incorrect" "${EP[@]}" install "$W/corrupt2" "$GD" --sign-key "$K/sign.key"
[ "$before" = "$(sha256sum "$GD/current.json" | cut -d" " -f1)" ] || fail "pointeur modifié"

echo "TOUT OK"
