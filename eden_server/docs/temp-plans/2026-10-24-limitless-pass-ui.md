# Limitless Pass UI + .ini rewards (lot 8 — C2/C3)

Plan parent : `docs/temp-plans/2026-10-24-lot8-prompt8.md` (sections C2+C3).
Périmètre : `clientmod/limitless/**`, `server/src/Eden.Core/Game/Limitless/**`,
`ZoneEntryFlow.Limitless.cs`, `GmHandlers.cs`, `Program.cs` (zone),
`reforged/EdenReforged/` (deploy). Interdit : autres `ZoneEntryFlow.*`,
`limitless/web/`, `client/game_.exe`.

## Preuves reverse collectées

- `rf_neobank @ 0x10008230/0x10008680` : mod Reforged prouvé en jeu qui
  installe `SetTimer(NULL,0,0x20,proc)` **sur le thread principal** →
  timer proc périodique safe pour CEGUI + `GetAsyncKeyState`.
- `rf_d3d9 @ 0x6d1cef60` (`eden_wndhook_is_foreground`) : gate
  `GetForegroundWindow` + `GetWindowThreadProcessId` == pid courant.
- `rf_game @ 0x00b7f550` (setVisible) : écrit `win+0xb8` → **d_visible
  = offset 0xb8**, lisible directement (rf_d3d9 écrit déjà ce byte pour
  la palette emoji — pattern prouvé).
- Layouts client : `DejaVu/FrameWindow` (252 usages) avec
  `TitlebarEnabled`, `CloseButtonEnabled`, `DragMovingEnabled`
  (ChannelWnd.layout `Root/ChannelWnd/EmojiPalette` = FrameWindow
  draggable prouvée) ; `DejaVu/MultiLineEditbox` avec `ReadOnly`,
  `MaxTextLength`, `TextParsingEnabled` (BattlefieldListUI.layout).
- `GameBalance.Load` (Eden.Core) : précédent de .ini custom
  `balance.ini` dans DATA_DIR (clé=valeur, `#` commentaire).
- `SendPacketAsync(ushort ncid, byte[] payload, ct)` — signature émission.

## Décisions

- **NCID 2033** = liste des paliers du pass :
  `[i32 charId][u16 tierCount]` puis `tierCount ×
  {[s16 level][i64 expReq][u16 textLen][text UTF-8]}` —
  text = résumé récompenses du palier (formaté serveur, statuts
  DONE/NEXT calculés côté client via level du 2032).
- **Bind** : `Ctrl+L` via `SetTimer(50ms)` installé sur le thread
  principal au premier 2032 reçu (pattern rf_neobank prouvé) +
  `GetAsyncKeyState` edge + gate foreground (pattern rf_d3d9).
- **Visibilité** : lecture `win+0xb8` (prouvé) → toggle `setVisible`.
- **.ini** : `limitless_pass.ini` dans DATA_DIR (`balance.ini` =
  précédent), sections `[level]` (courbe exp, override ll_level_cost)
  et `[reward]` (`level|code|kind|value|label`). Perks ini fusionnés
  aux perks DB (ids synthétiques ≤0, exclus du journal
  `player_limitless_perks` — FK sur limitless_perks.id).
- **Émission 2033** : après 2032 dans `SendLimitlessAsync`, dédupliquée
  par révision config/charId (pas de re-send par kill — contenu
  statique tant que config inchangée).
- **C2** : HUD `LimitlessHud` → `DejaVu/FrameWindow`
  (`TitlebarEnabled` + `DragMovingEnabled` + `CloseButtonEnabled=False`
  + `FrameEnabled/BackgroundEnabled=False`) → barre de titre = handle
  de drag, fenêtre transparente sinon. Layout-only, zéro risque code.

## Checklist

- [x] `LimitlessService` : Name/Description sur Perk, passIniPath,
      LoadPassIni, merge ini (perks+courbe), GetPassTiers, PassRevision,
      journal skip id≤0.
- [x] `ZoneEntryFlow.Limitless.cs` : EmitPassAsync (2033) + dédup
      révision + appel depuis SendLimitlessAsync.
- [x] `Program.cs` : ini path au ctor ; `GmHandlers.cs` : //ll_reload
      + emit pass après //ll_exp.
- [x] `limitless_pass.ini` (DATA_DIR = server_us/Data/db_decoded).
- [x] `limitless.c` : gen 2033, parse tiers, timer Ctrl+L, pass window,
      fill slots libres en drop jusqu'à 2048, borne curSize>2048.
- [x] `LimitlessPass.layout` + `LimitlessHud.layout` (FrameWindow drag).
- [x] `./clientmod/limitless/build.sh` → deploy dll+layouts.
- [x] `dotnet build` 0 err ; `dotnet test` vert (1084).
- [x] Doc `docs/limitless/` : format ini + wire 2033 + bind
      (LIMITLESS_CLIENT_HOOK.md + LIMITLESS_PASS_INI.md).
- [x] Cocher C2/C3 dans le plan lot8 + journal.

## Reste à valider en jeu (visuel)

- Fenêtre pass : rendu FrameWindow/MultiLineEditbox, Ctrl+L, bouton X.
- HUD : drag par la titlebar — si le skin la rend trop présente,
  fallback = drag maison via setProperty("UnifiedAreaRect") + sondage
  souris dans le timer (non implémenté).
- Ctrl+L pendant la saisie chat toggle aussi (limitation connue).
