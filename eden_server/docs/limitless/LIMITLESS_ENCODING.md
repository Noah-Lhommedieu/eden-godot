# Encodage des libellés Limitless (NCID 2033) — chaîne du mojibake

Pourquoi « Drop ×2 » s'affichait « Drop Ã—2 » (lot ≤9) puis « Drop ?2 »
(lot 10) dans la fenêtre Limitless Pass, et la solution définitive.

## La chaîne : seed → DB → wire → CEGUI → police

1. **Seed** `limitless/db/001_limitless.sql` : les perks d'exemple
   étaient libellées FR avec des caractères non-ASCII —
   `'Drop ×2'`, `'Exp Ultime ×2'`, `'Vitesse +10%'`, `'PV max +500'`.
   `×` = U+00D7 (signe multiplication), **pas** une lettre accentuée.
2. **DB** `fndb1.limitless_perks.name` : stockée en UTF-8 correct
   (`×` = octets `C3 97`) — les données n'étaient *pas*
   double-encodées (« UltimÃ© » en base : non, vérifié par dump).
3. **Wire** : `LimitlessEmitter.EmitPassAsync` sérialise
   `string.Join("; ", rewards.Name)` en UTF-8 (`TruncateUtf8` 200o).
   Le « ; » visible entre libellés est ce séparateur, pas un bug.
4. **CEGUI/police** : la fenêtre du mod utilise `F001-9` (police
   bitmap du jeu, charset ASCII/latin-1 étroit). Une séquence UTF-8
   multi-octets est rendue **octet par octet** : `C3 97` → glyphes
   « Ã » + « — » — le mojibake naît **au rendu**, pas sur le fil.
   Tout octet ≥ 0x80 émis est perdu d'avance.

## Ce qui a été fait (lot 11)

- **Données** : `limitless_perks.name` passés en EN ASCII pur
  (`Drop Rate x2`, `Ultimate EXP x2`, `Move Speed +10%`,
  `Max HP +500`, `Casting time -10%`) — seed SQL + UPDATE live.
  Rechargés par `ReloadAsync` (restart zone ou `//ll_reload`).
- **Filet de sécurité** : `LimitlessService.SanitizeLabel` (appliqué
  dans `GetPassTiers` → `EmitPassAsync`, chemin vérifié) fait
  désormais **deux passes** :
  1. équivalences ASCII pour les symboles typographiques courants
     (`×`→`x`, tirets `‐‑–—―`→`-`, guillemets `‘’“”«»`→`'`/`"`,
     `…`→`...`, `¹²³`→`123`, `°`→` deg`, `€`→`EUR`, `•·∙`→`-`) —
     **avant** la translittération, sinon un symbole non-lettre
     tombait en `?` (c'était le « Drop ?2 » du lot 10) ;
  2. décomposition FormD + retrait des marques diacritiques
     (é→e, ç→c) puis rejet `'?'` de tout caractère ≥ 0x80 restant.
  Puis relibellés EN historiques (« Cast -10% » → « Casting time
  -10% », « Vitesse » → « Move speed »).

## Règle pour les admins / futurs seeds

`limitless_perks.name` et le champ `libellé` de `limitless_pass.ini`
section `[reward]` sont affichés **tels quels** dans le client :
**ASCII 7 bits uniquement**. SanitizeLabel rattrape les oublis mais
un libellé propre en amont reste la meilleure garantie (les `?` de
rejet sont moches).
