#!/usr/bin/env python3
"""
extract_symbols.py — extraction des symboles/opcodes depuis les binaires serveur (ELF).

Les binaires sont NON strippés + DWARF : on récupère gratuitement :
  - les classes du moteur `lapis::` (nm -C)
  - les messages réseau `CNE_*` et leurs `GetNCID()`
  - les noms de fichiers source `.cc`

Sortie : docs/generated/<serveur>-symbols.txt (git-ignoré via docs/generated).

Usage :
  python3 tools/extract_symbols.py server_us/ZoneServer/ZoneServer
"""
import os
import re
import subprocess
import sys

BINARIES = {
    "TicketServer": "server_us/TicketServer/TicketServer",
    "GatewayServer": "server_us/GatewayServer/GatewayServer",
    "LoginServer": "server_us/LoginServer/LoginServer",
    "MissionServer": "server_us/MissionServer/MissionServer",
    "WorldServer": "server_us/WorldServer/WorldServer",
    "ZoneServer": "server_us/ZoneServer/ZoneServer",
}

OUT_DIR = "docs/generated"


def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True).stdout


def demangled_symbols(path):
    return run(["nm", "-C", path])


def source_files(path):
    raw = run(["strings", "-n", "6", path])
    return sorted(set(re.findall(r"[A-Za-z0-9_]+\.(?:cc|cpp|h)\b", raw)))


def net_messages(nm_out):
    # symboles démanglés contenant CNE_ (Net Events)
    msgs = sorted(set(re.findall(r"\bCNE_[A-Za-z0-9_]+", nm_out)))
    return msgs


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    target = sys.argv[1] if len(sys.argv) > 1 else None

    jobs = [(name, path) for name, path in BINARIES.items()
            if target is None or name == target]

    if not jobs:
        print(f"Aucun binaire correspondant à '{target}'. Choix : {list(BINARIES)}")
        sys.exit(1)

    for name, path in jobs:
        if not os.path.exists(path):
            print(f"[skip] {path} introuvable")
            continue
        print(f"[+] Analyse {name} -> {path}")
        nm_out = demangled_symbols(path)
        srcs = source_files(path)
        msgs = net_messages(nm_out)

        out_path = os.path.join(OUT_DIR, f"{name}-symbols.txt")
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(f"# {name} — symboles démanglés ({path})\n")
            f.write(nm_out)
            f.write("\n\n## Fichiers source détectés\n")
            f.write("\n".join(srcs))
            f.write("\n\n## Messages réseau CNE_* détectés\n")
            f.write("\n".join(msgs))
        print(f"    -> {out_path}  ({len(msgs)} messages CNE_)")


if __name__ == "__main__":
    main()
