#!/usr/bin/env python3
"""Audit des commandes GM — génère docs/generated/GM-COMMANDS.md.

Sources croisées :
- S_RootCmds.ini + S_GMToolCmds.ini (commandes natives + signature)
- server/src/Eden.Zone/Handlers/GmHandlers.cs (RegisterAsync/Register)
- server/src/Eden.Zone/ZoneEntryFlow.Admin.cs (cases du switch local)
- server/src/Eden.Zone/ZoneEntryFlow.cs (MarkImplemented)
- server/tests/Eden.Tests/*.cs (noms cités → « testée »)

Usage : tools/gm_audit.py (depuis la racine du dépôt, ou tout cwd).
"""
import os, re, sys, datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INI = os.path.join(ROOT, "server_us/Data/db_decoded/S_RootCmds.ini")
INI2 = os.path.join(ROOT, "server_us/Data/db_decoded/S_GMToolCmds.ini")
GMH = os.path.join(ROOT, "server/src/Eden.Zone/Handlers/GmHandlers.cs")
ADM = os.path.join(ROOT, "server/src/Eden.Zone/ZoneEntryFlow.Admin.cs")
FLOW = os.path.join(ROOT, "server/src/Eden.Zone/ZoneEntryFlow.cs")
TESTS = os.path.join(ROOT, "server/tests/Eden.Tests")
OUT = os.path.join(ROOT, "docs/generated/GM-COMMANDS.md")


def parse_ini(path):
    cmds = []
    if not os.path.exists(path):
        return cmds
    for line in open(path, encoding="big5", errors="replace"):
        line = line.strip()
        if not line or line.startswith("V."):
            continue
        f = line.split(",")
        if len(f) >= 3:
            sig = f[1].strip()
            name = sig.split()[0]
            cmds.append((name, sig, f[2].strip()))
    return cmds


def main():
    ini_cmds = parse_ini(INI) + parse_ini(INI2)
    gmh = open(GMH, encoding="utf8").read()
    adm = open(ADM, encoding="utf8").read()
    flow = open(FLOW, encoding="utf8").read()
    test_src = ""
    for fn in os.listdir(TESTS):
        if fn.endswith(".cs"):
            test_src += open(os.path.join(TESTS, fn), encoding="utf8").read()

    reg = set(re.findall(r'RegisterAsync\("(\w+)"', gmh))
    reg |= set(re.findall(r'\.Register\("(\w+)"', gmh))
    cases = set()
    for m in re.findall(r'case "(\w+)"', adm):
        cases.add(m)
    # MarkImplemented names : le tableau new[] du ctor avant
    # « _services.Gm.MarkImplemented(name) ».
    marked = set()
    pos = flow.find("_services.Gm.MarkImplemented(name)")
    if pos != -1:
        block = flow[:pos]
        block = block[block.rfind("new[]"):]
        marked = set(re.findall(r'"(\w+)"', block))

    names = {}
    for name, sig, acc in ini_cmds:
        names.setdefault(name.lower(), {"sig": sig, "access": acc,
                                        "ini": True})
    for n in reg | cases | marked:
        names.setdefault(n.lower(), {"sig": n, "access": "-", "ini": False})

    rows = []
    n_impl = 0
    for name in sorted(names):
        c = names[name]
        src = []
        if name in cases or name in marked:
            src.append("switch local")
        if name in reg:
            src.append("GmHandlers")
        if not src and c["ini"]:
            src.append("ini seul")
        impl = bool(reg and name in reg) or name in cases or name in marked
        if impl:
            n_impl += 1
        tested = "oui" if re.search(r'\b' + re.escape(name) + r'\b',
                                    test_src) else "-"
        rows.append((name, c["sig"], c["access"], "/".join(src),
                     "oui" if impl else "non", tested))

    with open(OUT, "w", encoding="utf8") as f:
        f.write("# Audit commandes GM — généré par tools/gm_audit.py\n\n")
        f.write(f"Généré : {datetime.date.today().isoformat()} — "
                f"{len(rows)} commandes connues "
                f"(S_RootCmds+S_GMToolCmds+maison), "
                f"{n_impl} implémentées.\n\n")
        f.write("| Commande | Signature | Accès | Source | Implémentée | "
                "Testée |\n|---|---|---|---|---|---|\n")
        for r in rows:
            f.write("| " + " | ".join(r) + " |\n")
    print(f"{OUT}: {len(rows)} commandes, {n_impl} implémentées")


if __name__ == "__main__":
    main()
