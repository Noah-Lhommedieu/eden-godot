# Hook client — Exp Ultime + Limitless Pass (NCIDs 2032/2033)

## Côté serveur

La zone émet deux opcodes custom (hors plage native ≤~900 — le client
natif les ignore proprement, index hors table de dispatch) :

- **NCID 2032** — état du perso (20 octets) :
  `[i32 charId][i64 exp][s16 level][u16 perkCount][i32 nextExp]`
  (`nextExp` = seuil du palier suivant, `int.MaxValue` = max).
  Émis à l'entrée en zone + après chaque gain d'Exp Ultime + sur
  `//ll_exp`.
- **NCID 2033** — liste des paliers du pass :
  `[i32 charId][u16 tierCount]` puis `tierCount ×
  {[s16 level][i64 expReq][u16 textLen][texte UTF-8]}`
  (`textLen` ≤ 200, texte = récap des récompenses du palier formaté
  côté serveur depuis `limitless_perks` BDD + `limitless_pass.ini`).
  Émis **dédupliqué par révision de config par session**
  (`PassRevision` bumpé à chaque `ReloadAsync`/`//ll_reload`) : à la
  première émission d'une connexion (un relog recrée l'état mod) et
  après tout reload — pas de re-send par kill (le statut DONE/NEXT
  est calculé côté mod depuis le level du 2032).

GM : `//limitless` (état en chat), `//ll_exp <n>` (grant + refresh
HUD), `//ll_reload` (recharge perks BDD + .ini + re-push 2033).

## Côté client — Reforged : `Limitless.dll` (V2)

`clientmod/limitless/limitless.c` (build `./build.sh` → déployé dans
`reforged/EdenReforged/mods/_testing/` + layouts dans
`reforged/EdenReforged/uidata/layouts/limitless/`).

1. **Interception** : le mod agrandit le `vector<Generator*>` du
   receiver zone (`DAT_010c3ea4`) à 2048 entrées par swap atomique ;
   index 2032 = HUD, index 2033 = liste des paliers, **tous** les
   slots libres ≤ 2047 = `DropOperator`. Justification (désasm
   `FUN_00d75da0` @ 0x00d75da0) : le dispatch **borne-check** le NCID
   (`ncid < (end-begin)/4` → NCID hors table ignoré proprement —
   c'est pourquoi le client natif/ancien mod ignorent 2033 sans
   crash) mais **déréfère le slot sans null-check**
   (`*vec[ncid]` → vtbl+4) — un slot NULL *dans* la table serait un
   wild jump, d'où le remplissage complet en DropOperator. Garde `-7` :
   `curSize > 2048` → abandon (le memcpy aurait débordé le buffer —
   bug latent V1 corrigé).
2. **HUD** (`LimitlessHud.layout` v3 — sombre natif) : racine
   `DejaVu/BuffWindow` (target FrameWindow) `TitlebarEnabled=False` +
   `FrameEnabled=True` → rendu `notitle_frame` = **bordure Tooltip
   sombre + fond TooltipMiddle** (style panneau buffs du jeu — plus de
   titlebar blanche `NewTitlebar*`, c'était le « dégueulasse » du lot
   11). Une ligne `DejaVu/TipTextLab` (`ULTIMATE Lv N EXP a/b Perks n`,
   fond sombre translucide `tl:90101824`) + `DejaVu/ProgressBar` dans
   un wrapper bordé `DejaVu/CastingBarImage` (`castingBar*`, cf.
   castingbarwnd.layout). **Drag maison** : `DragContainer` n'est pas
   un type enregistré (chaîne dans le binaire = testClassName
   `FUN_007ae830`, mais **aucune factory `CEGUI/DragContainer`** —
   type inconnu = layout fatal) → `PollHudDrag` sonde LMB dans le
   timer 50 ms + operators (gate foreground identique à Ctrl+L),
   déplacement par `setProperty("UnifiedPosition","{{0,x},{0,y}}")`,
   clampé au client rect ; position réinitialisée au layout
   (haut-centre 280×36) à chaque recréation de fenêtre. Updates :
   `LimitlessHud/Text` (setText) + `LimitlessHud/Bar`
   (CurrentProgress). Création **immédiate au premier 2032** ;
   recréation après perte détectée (relog) avec un court délai de
   grace (8 paquets) ; reset d'état au changement de charId.
   ⚠️ **le commentaire XML du .layout ne doit JAMAIS contenir
   `--`** : séquence illégale → le parser rejette tout le document →
   `ILoadLayout` retourne 0 → HUD invisible (régression lot10 :
   combinée au délai de grace de 40 paquets appliqué dès la première
   création, aucun `hud:` n'apparaissait même dans le log).
3. **Fenêtre Limitless Pass** (`LimitlessPass.layout`,
   `DejaVu/FrameWindow` draggable + bouton X natif) : ouverte/fermée
   par **Ctrl+L**. Contenu : `Info` (état 2032) + `Bar` (progression)
   + `Tiers` (MultiLineEditbox ReadOnly — paliers `[DONE ]/[NEXT ]/
   [-----]` formatés depuis le 2033) + `HowTo` (explications EN
   statiques du fonctionnement du pass).
   - Bind : `SetTimer(NULL,0,50ms,proc)` installé **sur le thread
     principal** depuis `operator()` → le timer proc s'exécute sur la
     pompe à messages du jeu (pattern rf_neobank
     `FUN_10008680 @ 0x10008680` + `SetTimer` idem rf_d3d9 palette
     emoji). `GetAsyncKeyState(VK_CONTROL)`+`VK_L` en edge-detection,
     gate foreground (`GetWindowThreadProcessId(fg)==pid`,
     `eden_wndhook_is_foreground @ 0x6d1cef60`). Repli : le même
     sondage tourne dans chaque `operator()` (toggle au prochain
     paquet si le timer ne tourne pas).
   - Toggle : `setVisible(!vis)` — `vis` lu à `win+0xb8` (d_visible,
     prouvé par le désasm de setVisible `FUN_00b7f550` :
     `param_1[0x2e]` ; rf_d3d9 écrit le même byte directement) → suit
     aussi le clic sur le bouton X du FrameWindow.
4. Bug historique corrigé : `wsprintfA("%lld")` rendait « Id » →
   formatage u64 maison (`fmt_u64`). Idem pour les expReq i64 du 2033.

## Limitations connues (à vérifier en jeu)

- **Ctrl+L se déclenche aussi pendant la saisie chat** — le sondage
  `GetAsyncKeyState` ne sait pas si un editbox CEGUI a le focus.
  Conflit réel si un raccourci natif utilise Ctrl+L (non observé).
- Le drag du HUD (PollHudDrag) hit-teste sur des constantes pixel
  (280×36) qui doivent rester synchrones avec le .layout — la position
  n'est **pas persistée** (retour haut-centre après relog/recréation).
  Un clic dans la zone du HUD est consommé par CEGUI (pas de click
  through vers le monde) — voulu, c'est la zone de drag.
- Client projet (`client/`) : 2032/2033 ignorés — `//limitless`
  en chat reste l'interface.

## À vérifier en jeu

- `limitless_mod.log` : `pkt2032`, `pkt2033 charId=… tiers=…`,
  `pass: timer Ctrl+L=<id>`, `pass: window=…`, `pass: shown/hidden`,
  **`hud: window=…`** (absence = loadLayout en échec → vérifier
  `hud: layout non charge` / `hud: root absente` et `CEGUI.log`),
  `hud: dropped x=… y=…` après un drag.
- HUD sombre déplaçable par clic-maintenu n'importe où dessus ; Ctrl+L
  ouvre la fenêtre, le X la
  ferme et Ctrl+L la rouvre ; tiers marqués DONE/NEXT correctement ;
  `//ll_exp 1000` met à jour les statuts.
- `CEGUI.log` si un layout ne charge pas.
