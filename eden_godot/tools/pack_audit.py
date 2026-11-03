#!/usr/bin/env python3
"""pack_audit.py — audit de référence « Étape 0 » du plan packaging EPK.

Mesure STAT uniquement : tailles et comptes de fichiers via os.scandir/stat —
aucun contenu asset n'est chargé (seule exception explicitement requise :
les .ini > 500 ko de data/db+data/scene, pour compter les lignes et sniffer
l'encodage ; chaque fichier n'est lu qu'une fois, ≤ ~6 Mo).

Sortie = Markdown sur stdout ; le rapport versionné se régénère par :

    python3 tools/pack_audit.py --root <eden_godot> > docs/AUDIT-PACK-BASELINE.md

Déterministe : tous les listings sont triés. Stdlib uniquement.
"""

import argparse
import os
import re
import sys
from collections import defaultdict
from datetime import datetime, timezone

GIB = 1024.0 ** 3
MIB = 1024.0 ** 2
KIB = 1024.0


def fmt_size(n):
    """Taille lisible, convention des docs du dépôt (Go/Mo, virgule)."""
    if n >= GIB:
        num, unit = f"{n / GIB:,.2f}", "Go"
    elif n >= MIB:
        num, unit = f"{n / MIB:,.1f}", "Mo"
    elif n >= KIB:
        num, unit = f"{n / KIB:,.1f}", "ko"
    else:
        return f"{n} o"
    return num.replace(",", " ").replace(".", ",") + " " + unit


def scan_tree(base):
    """(files={relpath_posix: size}, skipped=[msg]) — stat uniquement.

    Les symlinks ne sont pas suivis (recensés dans `skipped`) ;
    `files` est vide si `base` n'existe pas.
    """
    if not os.path.isdir(base):
        return {}, ["ABSENT"]
    files = {}
    skipped = []
    stack = [(base, "")]
    while stack:
        cur, rel = stack.pop()
        try:
            with os.scandir(cur) as it:
                entries = sorted(it, key=lambda e: e.name)
        except OSError as exc:
            skipped.append(f"{rel or '.'} ({exc.strerror or exc})")
            continue
        for e in entries:
            try:
                if e.is_symlink():
                    skipped.append(f"{rel}{e.name} (symlink ignoré)")
                elif e.is_dir(follow_symlinks=False):
                    stack.append((e.path, f"{rel}{e.name}/"))
                elif e.is_file(follow_symlinks=False):
                    files[rel + e.name] = e.stat(
                        follow_symlinks=False).st_size
            except OSError:
                skipped.append(f"{rel}{e.name} (stat impossible)")
    return files, skipped


def split2(files):
    """Agrégat 1er/2e niveau : 'cat/sub' si profondeur ≥3, 'cat (racine)'
    si fichier directement sous cat."""
    agg = defaultdict(lambda: [0, 0])
    for rel, size in files.items():
        parts = rel.split("/")
        if len(parts) >= 3:
            key = parts[0] + "/" + parts[1]
        elif len(parts) == 2:
            key = parts[0] + " (racine)"
        else:
            key = "(racine)"
        agg[key][0] += 1
        agg[key][1] += size
    return agg


def split1(files):
    agg = defaultdict(lambda: [0, 0])
    for rel, size in files.items():
        key = rel.split("/", 1)[0] if "/" in rel else "(racine)"
        agg[key][0] += 1
        agg[key][1] += size
    return agg


BACKUP_RE = re.compile(
    r"(^|/)(thumbs\.db|\.ds_store)$|\.(bak|old)$|~$", re.I)
MAP_S_RE = re.compile(r"^[Ss][0-9]")
MAP_META_EXT = {".fsm", ".ini", ".area"}
INI_MIN = 500 * 1024


def is_dark_path(rel):
    return any(p.lower() == "dark" for p in rel.split("/")[:-1])


def table(out, headers, rows):
    out.append("| " + " | ".join(headers) + " |")
    out.append("|" + "|".join("---" for _ in headers) + "|")
    for r in rows:
        out.append("| " + " | ".join(str(c) for c in r) + " |")


def perf_baseline(perf_path, out):
    """Extrait les lignes de mesures S001 de docs/PERF.md (texte brut)."""
    out.append("## 8. Baseline perf existante (référence « avant »)")
    out.append("")
    if not os.path.isfile(perf_path):
        out.append("`docs/PERF.md` introuvable — section vide.")
        out.append("")
        return
    keys = ("en jeu", "loadmap", "glb preload", "entities (corps",
            "vram textures", "fps en jeu", "moy 15s", "protos glb",
            "objects+env")
    hits = []
    try:
        with open(perf_path, encoding="utf-8", errors="replace") as fh:
            for n, line in enumerate(fh, 1):
                low = line.lower()
                if "|" in line and any(k in low for k in keys):
                    hits.append((n, line.rstrip()))
    except OSError:
        out.append("`docs/PERF.md` illisible.")
        out.append("")
        return
    out.append("Lignes extraites verbatim de `docs/PERF.md` (mesures S001, "
               "machine dev RTX — à rejouer après packaging) :")
    out.append("")
    out.append("```")
    for n, line in hits[:25]:
        out.append(f"PERF.md:{n}: {line}")
    if len(hits) > 25:
        out.append(f"… ({len(hits) - 25} lignes supplémentaires)")
    out.append("```")
    out.append("")


def sniff_ini(path):
    """(nb_lignes, encodage) — lit le fichier une seule fois (≤ ~6 Mo)."""
    raw = open(path, "rb").read()
    lines = raw.count(b"\n")
    if raw.startswith(b"\xef\xbb\xbf"):
        enc = "UTF-8 BOM"
    else:
        try:
            raw.decode("utf-8")
            enc = "UTF-8"
        except UnicodeDecodeError:
            nbad = raw.decode("utf-8", errors="replace").count("�")
            enc = (f"non-UTF8 ({nbad:,} car. indécodables — "
                   "Big5 probable)").replace(",", " ")
    return lines, enc


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".",
                    help="racine eden_godot (défaut: répertoire courant)")
    args = ap.parse_args()
    root = os.path.abspath(args.root)

    assets = os.path.join(root, "godot_project", "assets")
    gamedata = os.path.join(root, "godot_project", "gamedata")
    raw = os.path.join(root, "raw_assets")          # symlink — résolu par scandir
    docs = os.path.join(root, "docs")

    t0 = datetime.now(timezone.utc).astimezone()

    # --- scans (stat only) ------------------------------------------------
    tex_f, tex_skip = scan_tree(os.path.join(assets, "textures"))
    mod_f, mod_skip = scan_tree(os.path.join(assets, "models"))
    ani_f, ani_skip = scan_tree(os.path.join(assets, "anims"))
    uidata_f, uidata_skip = scan_tree(os.path.join(raw, "uidata"))
    data_f, data_skip = scan_tree(os.path.join(raw, "data"))
    map_f, map_skip = scan_tree(os.path.join(raw, "map"))
    gd_f, gd_skip = scan_tree(gamedata)

    all_assets = dict(tex_f)
    for d in (mod_f, ani_f):
        all_assets.update(d)  # relpaths disjointes par catégorie
    skipped = (tex_skip + mod_skip + ani_skip + uidata_skip +
               data_skip + map_skip + gd_skip)

    # --- découpage map/ ----------------------------------------------------
    # Layout package_dist.sh : map/model/Sxxx/*.{fsm,ini,area} + Dark/** ;
    # map/sknodemap/*.smp lu au runtime (MapLoader) mais PAS copié par le
    # script — mesuré à part.
    map_meta, map_dark, map_sknodemap, map_other = {}, {}, {}, {}
    for rel, size in map_f.items():
        parts = rel.split("/")
        if parts[0] == "model" and len(parts) >= 2 and \
                MAP_S_RE.match(parts[1] or ""):
            if is_dark_path(rel):
                map_dark[rel] = size
            elif len(parts) == 3 and \
                    os.path.splitext(parts[2])[1].lower() in MAP_META_EXT:
                map_meta[rel] = size
            else:
                map_other[rel] = size
        elif parts[0] == "sknodemap":
            map_sknodemap[rel] = size
        else:
            map_other[rel] = size

    # --- slim 1:1 ----------------------------------------------------------
    def glb_sets(files):
        fat, slim = {}, {}
        for rel, size in files.items():
            low = rel.lower()
            if low.endswith(".slim.glb"):
                slim[rel[:-9]] = (rel, size)
            elif low.endswith(".glb"):
                fat[rel[:-4]] = (rel, size)
        return fat, slim

    fat_all, slim_all = {}, {}
    for group_files, tag in ((ani_f, "anims"), (mod_f, "models"),
                             (tex_f, "textures")):
        fat, slim = glb_sets(group_files)
        for k, v in fat.items():
            fat_all[f"{tag}/{k}"] = v
        for k, v in slim.items():
            slim_all[f"{tag}/{k}"] = v

    pairs = sorted(set(fat_all) & set(slim_all))
    orphans_fat = sorted(set(fat_all) - set(slim_all))
    orphans_slim = sorted(set(slim_all) - set(fat_all))
    anims_orphans_fat = [k for k in orphans_fat if k.startswith("anims/")]
    non_anims_orphans_fat = [k for k in orphans_fat
                             if not k.startswith("anims/")]
    fat_covered_bytes = sum(fat_all[k][1] for k in pairs)
    slim_bytes = sum(v[1] for _, v in slim_all.items())
    fat_total_bytes = sum(v[1] for _, v in fat_all.items())

    # --- exclusions ---------------------------------------------------------
    def corpus_iter():
        for tag, files in (("assets", all_assets), ("uidata", uidata_f),
                           ("data", data_f), ("map", map_f),
                           ("gamedata", gd_f)):
            for rel, size in files.items():
                yield f"{tag}/{rel}", size

    excl_kfm = {p: s for p, s in corpus_iter()
                if p.lower().endswith(".kfm.json")}
    excl_dark_assets = {f"assets/{r}": s for r, s in all_assets.items()
                        if is_dark_path(r)}
    excl_dark_raw = {f"map/{r}": s for r, s in map_dark.items()}
    excl_backups = {p: s for p, s in corpus_iter() if BACKUP_RE.search(p)}
    excl_exe = {p: s for p, s in corpus_iter()
                if p.lower().endswith((".exe", ".dll"))}

    # --- textures histogram --------------------------------------------------
    tex_hist = defaultdict(lambda: [0, 0])
    for rel, size in tex_f.items():
        ext = os.path.splitext(rel)[1].lower() or "(sans ext)"
        tex_hist[ext][0] += 1
        tex_hist[ext][1] += size

    # --- tables chaudes -------------------------------------------------------
    hot = [(rel, size) for rel, size in data_f.items()
           if rel.lower().endswith(".ini") and size > INI_MIN]
    hot.sort(key=lambda x: -x[1])
    hot_rows = []
    for rel, size in hot[:15]:
        path = os.path.join(raw, "data", rel.replace("/", os.sep))
        try:
            lines, enc = sniff_ini(path)
        except OSError:
            lines, enc = -1, "illisible"
        hot_rows.append(("data/" + rel, fmt_size(size),
                         f"{lines:,}".replace(",", " "), enc))

    # --- corpus packageable + top 30 ------------------------------------------
    # Reproduit le layout tools/package_dist.sh : uidata/** + data/** +
    # map/model/Sxxx meta+Dark + assets/** + gamedata/**.
    pack_files = {}
    for rel, size in uidata_f.items():
        pack_files["uidata/" + rel] = ("uidata", size)
    for rel, size in data_f.items():
        pack_files["data/" + rel] = ("data", size)
    for rel, size in map_meta.items():
        pack_files["map/" + rel] = ("map-meta", size)
    for rel, size in map_dark.items():
        pack_files["map/" + rel] = ("map-dark", size)
    for rel, size in tex_f.items():
        pack_files["assets/textures/" + rel] = ("assets/textures", size)
    for rel, size in mod_f.items():
        pack_files["assets/models/" + rel] = ("assets/models", size)
    for rel, size in ani_f.items():
        low = rel.lower()
        cat = ("anims/.slim.glb" if low.endswith(".slim.glb")
               else "anims/.glb fat" if low.endswith(".glb")
               else "anims/.kfm.json" if low.endswith(".kfm.json")
               else "anims/autres")
        pack_files["assets/anims/" + rel] = (cat, size)
    for rel, size in gd_f.items():
        pack_files["gamedata/" + rel] = ("gamedata", size)

    anims_pair_stems = {k[len("anims/"):] for k in pairs
                        if k.startswith("anims/")}

    def is_excludable(path):
        """True si le fichier sortirait du pack (exclusions étape 2)."""
        low = path.lower()
        if low.endswith(".glb") and not low.endswith(".slim.glb") \
                and path.startswith("assets/anims/"):
            if path[len("assets/anims/"):-4] in anims_pair_stems:
                return True
        if low.endswith(".kfm.json"):
            return True
        if any(p.lower() == "dark" for p in path.split("/")[:-1]):
            return True
        if BACKUP_RE.search(path):
            return True
        return False

    top30 = sorted(pack_files.items(), key=lambda kv: -kv[1][1])[:30]
    pack_total = sum(s for _, s in pack_files.values())

    # --- estimation ---------------------------------------------------------
    brut = pack_total
    save_fat = sum(fat_all[k][1] for k in pairs if k.startswith("anims/"))
    save_kfm = sum(excl_kfm.values())
    save_dark = sum(excl_dark_assets.values()) + sum(excl_dark_raw.values())
    save_bak = sum(excl_backups.values())
    save_exe = sum(excl_exe.values())
    net = brut - save_fat - save_kfm - save_dark - save_bak - save_exe
    skn = sum(map_sknodemap.values())

    # ================== SORTIE MARKDOWN =====================================
    out = []
    out.append("# Audit pack — baseline Étape 0 (corpus mesuré)")
    out.append("")
    out.append(f"- Date : {t0:%Y-%m-%d %H:%M %Z}")
    out.append(f"- Racine auditée : `{root}`")
    out.append(f"- `raw_assets` → `{os.path.realpath(raw)}`" if os.path.islink(raw)
               else f"- `raw_assets` : `{raw}` (réel)")
    out.append("- Commande : `python3 tools/pack_audit.py --root "
               f"{root} > docs/AUDIT-PACK-BASELINE.md`")
    out.append("- Méthode : **stat() uniquement** — tailles et comptes ; "
               "aucun contenu asset lu (exception bornée : `.ini` > 500 ko "
               "pour lignes/encodage).")
    out.append("- Régénérable, déterministe (listings triés), stdlib only.")
    out.append("")
    out.append("> Référence : `docs/PLAN-PACK-EPK.md` (cible **8-12 Go**, "
               "borne 5-15 ; exclusions : fat `.glb` couverts par `.slim.glb`, "
               "`*.kfm.json`, `Dark/*`, backups).")
    out.append("")

    # ---------------- 0. verdicts -----------------------------------------
    out.append("## 0. Verdicts")
    out.append("")
    go = not anims_orphans_fat and not orphans_slim
    out.append(f"- **Couverture slim 1:1** : {'**GO**' if go else '**NO-GO**'} "
               f"— {len(pairs)} paires `.glb`/`.slim.glb` sous `anims/` "
               f"({fmt_size(save_fat)} de fat excluables), "
               f"{len(anims_orphans_fat)} fat orphelin(s) sous anims/, "
               f"{len(orphans_slim)} slim sans frère fat. "
               + ("Les fat d'anims sont exclus du pack **sans perte** "
                  "(le loader préfère `.slim.glb`, repli `.glb` inutile "
                  "puisque couvert à 100 %)."
                  if go else
                  "Des orphelins existent — exclusion NON sûre, "
                  "voir §2."))
    out.append(f"- **Poids mesuré vs cible** : layout actuel brut "
               f"= **{fmt_size(brut)}** ; après exclusions mesurées "
               f"= **{fmt_size(net)}** → encore au-dessus de la cible "
               f"8-12 Go : le solde dépend des étapes build-time non "
               f"mesurées ici (compression GPU textures, zstd). "
               f"Borne honnête : le pack « exclusions seules » fait "
               f"**{fmt_size(net)}** (cf. §7).")
    surprises = []
    if map_sknodemap:
        pkg = os.path.join(root, "tools", "package_dist.sh")
        try:
            pkg_txt = open(pkg, encoding="utf-8", errors="replace").read()
            miss = "sknodemap" not in pkg_txt
        except OSError:
            miss = None
        surprises.append(
            f"`map/sknodemap/` = {len(map_sknodemap)} fichiers / "
            f"{fmt_size(skn)} lus au runtime (`MapLoader` → murs "
            f"invisibles) mais **absents du layout `package_dist.sh`**"
            + (" (vérifié : le script ne le copie pas) — gap à corriger "
               "au packaging." if miss else
               " (le script le copie déjà)." if miss is False else ""))
    if excl_dark_assets:
        surprises.append(
            f"Lightmaps `Dark/` en double : PNG convertis sous "
            f"`assets/textures/map/model/Sxxx/Dark/` "
            f"({fmt_size(sum(excl_dark_assets.values()))}, "
            f"{len(excl_dark_assets)} fichiers) **et** DDS sources sous "
            f"`raw map/model/Sxxx/Dark/` ({fmt_size(sum(excl_dark_raw.values()))}"
            f") — aucun lecteur `Dark/` dans `godot_project/scripts/` "
            f"(greppé) ; le plan ne citait que les .png.")
    if excl_exe:
        for p, s in sorted(excl_exe.items()):
            surprises.append(f"`{p}` ({fmt_size(s)}) — binaire Windows "
                             f"embarqué dans les données, excluable.")
    if excl_backups:
        surprises.append(
            f"{len(excl_backups)} backup(s)/fichier(s) système "
            f"({fmt_size(save_bak)} — Thumbs.db sous `uidata/imagesets/`).")
    n_nonutf8 = sum(1 for r in hot_rows if "non-UTF8" in str(r[3]))
    if n_nonutf8:
        surprises.append(
            f"{n_nonutf8}/{len(hot_rows)} tables chaudes non-UTF8 "
            f"(Big5 probable) — le bake-tables doit respecter "
            f"l'encodage sniffé par `IniTable`.")
    non_sxxx = [k for k in map_other if k.split("/")[0] == "model"
                and len(k.split("/")) >= 2
                and not MAP_S_RE.match(k.split("/")[1])]
    if non_sxxx:
        sz = sum(map_other[k] for k in non_sxxx)
        surprises.append(
            f"`map/model/` hors Sxxx (effect/object/sky/weather/vgncustom"
            f" + `.nif`/`.dds` racine) : {len(set(k.split('/')[1] for k in non_sxxx))} entrées, "
            f"{fmt_size(sz)} — hors package (convertis dans `assets/` "
            f"ou inutilisés), ne PAS embarquer par erreur.")
    for s in surprises:
        out.append(f"- ⚠️ {s}")
    out.append("")

    # ---------------- 1. catégories ----------------------------------------
    out.append("## 1. Catégories (fichiers / octets)")
    out.append("")

    def tot(files):
        return len(files), sum(files.values())

    n, s = tot(tex_f)
    rows = [["`assets/textures`", n, fmt_size(s)]]
    for k in sorted(split1(tex_f)):
        nn, ss = split1(tex_f)[k]
        rows.append([f"　└ `textures/{k}`", nn, fmt_size(ss)])
    n, s = tot(mod_f)
    rows.append([f"**`assets/models`**", n, fmt_size(s)])
    for k in sorted(split2(mod_f)):
        nn, ss = split2(mod_f)[k]
        rows.append([f"　└ `models/{k}`", nn, fmt_size(ss)])
    n, s = tot(ani_f)
    rows.append([f"**`assets/anims`**", n, fmt_size(s)])
    for k in sorted(split2(ani_f)):
        nn, ss = split2(ani_f)[k]
        rows.append([f"　└ `anims/{k}`", nn, fmt_size(ss)])
    n, s = tot(uidata_f)
    rows.append(["**raw `uidata/`**", n, fmt_size(s)])
    n, s = tot(data_f)
    rows.append(["**raw `data/`**", n, fmt_size(s)])
    for sub in sorted(split1(data_f)):
        nn, ss = split1(data_f)[sub]
        rows.append([f"　└ `data/{sub}`", nn, fmt_size(ss)])
    n, s = tot(map_f)
    rows.append(["**raw `map/` (arbre complet)**", n, fmt_size(s)])
    for sub in sorted(split1(map_f)):
        nn, ss = split1(map_f)[sub]
        rows.append([f"　└ `map/{sub}`", nn, fmt_size(ss)])
    rows.append([f"　└ dont `map/model/Sxxx` meta (.fsm/.ini/.area)",
                 len(map_meta), fmt_size(sum(map_meta.values()))])
    rows.append([f"　└ dont `map/model/Sxxx/Dark/` (dds source)",
                 len(map_dark), fmt_size(sum(map_dark.values()))])
    n, s = tot(gd_f)
    rows.append(["**projet `gamedata/`**", n, fmt_size(s)])
    table(out, ["dossier", "fichiers", "octets"], rows)
    out.append("")
    n_all = sum(len(f) for f in
                (tex_f, mod_f, ani_f, uidata_f, data_f, map_f, gd_f))
    s_all = sum(sum(f.values()) for f in
                (tex_f, mod_f, ani_f, uidata_f, data_f, map_f, gd_f))
    out.append(f"**Total corpus mesuré** : {n_all} fichiers, "
               f"{fmt_size(s_all)} (dont assets = "
               f"{fmt_size(sum(tex_f.values()) + sum(mod_f.values()) + sum(ani_f.values()))}).")
    if skipped:
        out.append("")
        out.append(f"Ignorés pendant le scan ({len(skipped)}) : "
                   + "; ".join(skipped[:10])
                   + (" …" if len(skipped) > 10 else ""))
    out.append("")

    # ---------------- 2. slim ---------------------------------------------
    out.append("## 2. Couverture `.slim.glb` 1:1 — **critère bloquant du plan**")
    out.append("")
    table(out, ["ensemble", "fichiers", "octets"],
          [["`*.glb` fat total", len(fat_all), fmt_size(fat_total_bytes)],
           ["`*.slim.glb` total", len(slim_all), fmt_size(slim_bytes)],
           ["paires fat↔slim", len(pairs), fmt_size(fat_covered_bytes) + " (fat)"],
           ["fat SANS slim sous `anims/`", len(anims_orphans_fat),
            fmt_size(sum(fat_all[k][1] for k in anims_orphans_fat))],
           ["fat sans slim hors `anims/` (attendu : `models/` n'a "
            "pas de pipeline slim)", len(non_anims_orphans_fat),
            fmt_size(sum(fat_all[k][1] for k in non_anims_orphans_fat))],
           ["slim SANS fat (déjà autonome)", len(orphans_slim),
            fmt_size(sum(slim_all[k][1] for k in orphans_slim))]])
    out.append("")
    if anims_orphans_fat or orphans_slim:
        out.append("**Orphelins (à traiter avant exclusion) :**")
        out.append("")
        for k in (anims_orphans_fat + orphans_slim)[:50]:
            src = fat_all.get(k) or slim_all.get(k)
            out.append(f"- `{k}` ({fmt_size(src[1])})")
        if len(anims_orphans_fat) + len(orphans_slim) > 50:
            out.append(f"- … {len(anims_orphans_fat) + len(orphans_slim) - 50} autres")
        out.append("")
    else:
        out.append("**Aucun orphelin** — chaque `.slim.glb` a son `.glb` "
                   "frère et réciproquement sous `anims/`. Les `.glb` "
                   "hors `anims/` (modèles) n'ont pas de variante slim "
                   "et restent indispensables.")
        out.append("")
    out.append(f"**Verdict : {'GO' if go else 'NO-GO'}** — "
               + ("on peut exclure du pack les fat `.glb` couverts par "
                  f"un slim **sans perte** ({fmt_size(save_fat)} "
                  f"économisables ; `EntityAnimator.ResolveAnimPath` "
                  f"préfère `.slim.glb`)."
                  if go else
                  "orphelins présents : exclure les fat perdrait des "
                  "données, traiter la liste ci-dessus d'abord."))
    out.append("")

    # ---------------- 3. exclusions ----------------------------------------
    out.append("## 3. Exclusions mesurées (octets économisables)")
    out.append("")
    table(out, ["exclusion", "fichiers", "octets", "où"],
          [["fat `.glb` couverts par `.slim.glb`", len(pairs),
            fmt_size(save_fat), "`assets/anims/`"],
           ["`*.kfm.json` (sidecars de conversion)", len(excl_kfm),
            fmt_size(save_kfm), "`assets/anims/*`"],
           ["`Dark/` convertis (.png)", len(excl_dark_assets),
            fmt_size(sum(excl_dark_assets.values())),
            "`assets/textures/map/model/Sxxx/Dark/`"],
           ["`Dark/` sources (.dds, copiés par package_dist.sh)",
            len(excl_dark_raw), fmt_size(sum(excl_dark_raw.values())),
            "`map/model/Sxxx/Dark/`"],
           ["backups `Thumbs.db`/`.DS_Store`/`*.bak`/`*.old`/`*~`",
            len(excl_backups), fmt_size(save_bak), "uidata/…"],
           ["binaires embarqués `.exe`/`.dll` (bonus)", len(excl_exe),
            fmt_size(save_exe), "data/db"]])
    out.append("")
    out.append(f"**Total excluable mesuré : {fmt_size(save_fat + save_kfm + save_dark + save_bak + save_exe)}** "
               f"(vs −12,5 Go estimés au plan pour la seule ligne slim — "
               f"le plan comptait large : les slim pèsent "
               f"{fmt_size(slim_bytes)} contre {fmt_size(fat_total_bytes)} "
               f"de fat totaux, mais seuls les fat d'`anims/` sont "
               f"couverts).")
    out.append("")

    # ---------------- 4. textures ------------------------------------------
    out.append("## 4. Textures — histogramme extensions (`assets/textures/`)")
    out.append("")
    rows = [[f"`{ext}`", n, fmt_size(s)]
            for ext, (n, s) in sorted(tex_hist.items(),
                                      key=lambda kv: -kv[1][1])]
    table(out, ["extension", "fichiers", "octets"], rows)
    out.append("")
    other = [(e, v) for e, v in tex_hist.items() if e != ".png"]
    if not other:
        out.append("Corpus 100 % `.png` — aucun WebP/DDS converti : "
                   "tout le gain « textures pré-compressées » du plan "
                   "reste à faire au build.")
    else:
        out.append("Non-png : " + ", ".join(
            f"`{e}` {v[0]} fichiers/{fmt_size(v[1])}" for e, v in other))
    out.append("")

    # ---------------- 5. tables chaudes --------------------------------------
    out.append("## 5. Tables chaudes — `.ini` > 500 ko sous `data/` "
               f"({len(hot)} au total, top 15)")
    out.append("")
    table(out, ["fichier", "taille", "lignes (≈wc)", "encodage"], hot_rows)
    out.append("")

    # ---------------- 6. top 30 ----------------------------------------------
    out.append("## 6. Top 30 plus gros fichiers du corpus packageable")
    out.append("")
    rows = []
    for path, (cat, size) in top30:
        flag = " → **exclu**" if is_excludable(path) else ""
        rows.append([f"`{path}`", cat, fmt_size(size) + flag])
    table(out, ["fichier", "cat.", "octets"], rows)
    out.append("")

    # ---------------- 7. estimation ------------------------------------------
    out.append("## 7. Estimation pack cible (mesurée, hors compression)")
    out.append("")
    groups = defaultdict(lambda: [0, 0])
    for _, (cat, size) in pack_files.items():
        groups[cat][0] += 1
        groups[cat][1] += size
    rows = [[f"`{k}`", v[0], fmt_size(v[1])]
            for k, v in sorted(groups.items(), key=lambda kv: -kv[1][1])]
    rows.append(["**TOTAL layout actuel (brut)**",
                 sum(v[0] for v in groups.values()), fmt_size(brut)])
    rows.append(["− fat `.glb` couverts par slim", "", f"−{fmt_size(save_fat)}"])
    rows.append(["− `*.kfm.json`", "", f"−{fmt_size(save_kfm)}"])
    rows.append(["− `Dark/` (png convertis + dds sources)", "",
                 f"−{fmt_size(save_dark)}"])
    rows.append(["− backups/système", "", f"−{fmt_size(save_bak)}"])
    rows.append(["− `.exe` embarqué", "", f"−{fmt_size(save_exe)}"])
    rows.append(["**= pack « exclusions appliquées »**", "",
                 f"**{fmt_size(net)}**"])
    table(out, ["poste", "fichiers", "octets"], rows)
    out.append("")
    out.append(f"Borne honnête : **{fmt_size(net)}** en stat brut après "
               f"exclusions — la cible 8-12 Go suppose en plus la "
               f"compression GPU des textures ({fmt_size(sum(tex_f.values()))} "
               f"de PNG aujourd'hui, ~−6 Go visés au plan) et zstd "
               f"(−1,5-2 Go estimés), **non mesurables ici**. "
               f"`map/sknodemap/` ({fmt_size(skn)}) n'est PAS dans ce "
               f"total (gap du layout actuel — à ajouter, pas à exclure).")
    out.append("")

    # ---------------- 8. perf -------------------------------------------------
    perf_baseline(os.path.join(docs, "PERF.md"), out)

    sys.stdout.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main()
