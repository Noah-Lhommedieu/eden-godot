# Launcher & protection d'assets — design (chantier 2)

> Réponse à : « comment un launcher peut faire des majs client
> distant par rapport aux .ini, sécuriser le contenu (chiffrement)
> sans tout casser, casser/remplacer les .pkg en facilitant
> l'édition ».

## 1. Format assets — fait acquis

- `pkg.idx` = 592o/record : nom (+0x04) / dir (+0x108) /
  pkg# (+0x210) / offset (+0x21c) / taille zlib (+0x224).
- `pkgNNN.pkg` = concaténation de tranches zlib, une par fichier.
- `PkgIndexService` (lecture) + `PkgWriter` (édition en place vers
  un `pkg999.pkg` patch) implémentés dans Limitless.Studio.
- Les `.ini` (S_Item etc.) sont **côté serveur** — le client n'en a
  pas besoin en clair : ils vivent dans les pkg (`data\db\*.ini`
  dans les pkg aussi, vus dans pkg.idx : `NpcAvatar.ini` dir
  `data\db\`).

## 2. Mises à jour distantes (launcher)

Modèle manifeste+diff, éprouvé :

```
launcher ─GET→ https://cdn.limitless/manifest.json
   { "version": 42,
     "files": [ {"path":"pkg/pkg999.pkg","sha256":"…","size":N},
                {"path":"pkg/pkg.idx","sha256":"…","size":M} ] }
launcher → compare sha256 locaux → télécharge SEULEMENT les
           fichiers modifiés (HTTP range si besoin) → vérifie hash
           → swap atomique (fichier .new + rename).
```

- Le serveur de build (Studio ou CI) génère `pkg999.pkg` +
  `pkg.idx` mis à jour + manifest signé (ed25519 — la clé publique
  embarquée dans le launcher ; rejet d'un manifest non signé =
  anti-poisoning).
- Les `.ini` serveur n'ont pas besoin de maj client — SEULS les
  assets client (icônes, layouts, modèles, `.ini` client dans pkg)
  voyagent. Éditer S_Item côté serveur = aucun déploiement client.
- Atomicité : jamais d'écriture in-place des fichiers live —
  `pkg999.pkg.new` complet + rename (le client lit la version
  précédente pendant la maj).

## 3. Protection du contenu (réaliste)

Le client vanilla lit zlib+idx en clair — le chiffrement **fort**
exige de patcher le lecteur client (hook d3d9/gameFix — même canal
que les QoL Reforged). Options ordonnées :

### Option A — container patch chiffré + hook (fort)
- `pkg999.pkg` chiffré (AES-256-GCM ou XChaCha20) + `pkg.idx`
  chiffré ; le hook Limitless (d3d9) détourne le reader pkg pour
  déchiffrer à la volée. Contenu jamais en clair sur disque.
- Coût : reverse du reader pkg natif + hook stable. Fort mais
  lourd — phase 2.

### Option B — obfuscation du container (moyen, simple)
- Format maison pour pkg999 : XOR/shuffle des slices + idx privé
  chargé par le hook ou un shim launcher. Ralentit le vol casual,
  n'empêche pas un RE déterminé.
- Plus simple : les slices zlib ont déjà un overhead — on peut
  XOR les premiers octets + corriger via hook.

### Option C — distribution .ini serveur uniquement (déjà gagné)
- Le contenu sensible (stats items, drop tables) reste **uniquement
  côté serveur** (server_us + fndb1) — jamais distribué. Le client
  ne voit que l'art (icônes/modèles) + données UI nécessaires.
- C'est la protection réelle : ne jamais livrer ce qui est sensible.
  Recommandé comme base ; A/B = durcissement au-dessus.

## 4. « Casser/remplacer les .pkg » — fait

`PkgWriter.Replace(nom, octets)` :
1. zlib l'asset modifié → append à `pkg999.pkg` (créé si absent).
2. Met à jour le record `pkg.idx` EN PLACE (pkg#/offset/size).
3. Atomicité côté outil : backup `pkg.idx.bak` + écriture .new +
   rename en prod.

Le Studio édite donc un asset (icône, modèle, ini client) et
produit un `pkg999.pkg` + `pkg.idx` prêts à publier via manifest.
Pas de repack total, pas de format cassé — le client lit le patch
exactement comme vanilla.

## 5. Questions ouvertes

1. Reverse du reader pkg natif pour le hook (option A) — à faire
   si le chiffrement fort devient nécessaire.
2. Le client tolère-t-il pkg999 hors plage vanilla ? Le loader
   ouvre probablement `pkg%03d.pkg` par le champ pkg# du record —
   pkg999 devrait marcher (à valider live).
3. Signature du manifest : clé ed25519 Limitless à générer.
