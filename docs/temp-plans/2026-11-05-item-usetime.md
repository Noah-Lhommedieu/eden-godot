# 2026-11-05 — Canalisation d'usage d'item (407/408)

## Preuves natives (zone/decompiled.c)

- `CItemQuery::Deserialize` @0x8602cf0 — comptage colonnes ancré 4× :
  col8→+0x20 kind, col10→+0x2c trainmask, col11→+0x30 flags ll,
  col12→+0x38 usetype, col13→+0x3c, strtol col14→+0x3e (req level),
  strtol col15→+0x3f (max level), col16→+0x40, col17→+0x44,
  **col18→+0x48 u64** (masque restriction limit+0/+4),
  col19→+0x50, **col20→+0x54 (u32 use-time)**, col21→+0x58 cd,
  col22→+0x5c cd-group, col23→+0x60 use-enchant.
- `CItemFactory::DoUseItem` @0x80b8a10 : `piVar13[0x15]` (=+0x54) == 0
  → `DoUseItemAfterCheck` immédiat ; sinon si char+0x90 == 0 →
  émission **407** `[u32 itemId][s16 contIdx][s16 slot]`,
  `char+0x90 = control`, `CEventUseItemTime` planifié à
  `+low16(+0x54) décisecondes` (ctor @0x819cd10 : sec=p/10,
  µs=(p%10)×100000), `LifeEntityDo(char,6)` (état canalisation).
- `CEventUseItemTime::Execute` @0x819cb30 : à l'échéance, si
  char+0x90 toujours le même control → re-fetch container[slot] :
  NULL→texte 0x5f7 ; serial changé→release ; identique→
  `DoUseItemAfterCheck`. Release du control + char+0x90=0.
- `CPlayerFactory::CheckCastBreak` @0x80f5790 : si char+0x90 ≠ 0 →
  cancel event (vfunc+0x3c), release, **408** (corps vide),
  `RemoveDo(state 6)`. Appelé depuis MoveSelf / MeleeAttack.
- `CItemFactory::UseItemParam` @0x80b9490 : controls pour UseType
  1/2/3/5/6 seulement — **2228 items ont col20≠0 dans S_Item
  (db_decoded), ~2225 UseType 2** (kind 44 outils, 48 boîtes :
  confiture, graines, feux d'artifice, manuels, cartes au trésor —
  1 à 100 ds). Chemin vivant.
- Masque de classes équipement = **col13 → +0x3c → CItemLimit+8
  (u16)** lu dans `CheckLimit` @0x80b5140 — non parsé côté C#
  (distinct du u64). `ItemDef.ClassMask = row[18]` est **correct** :
  c'est le u64 +0x48, fidèle à `SaveAuction` natif (filtre HdV) —
  la suspicion initiale de bug était erronée.

## Implémentation (faite)

Serveur (`eden_server/server/src/Eden.Zone/`) :

- `ItemParser` : `UseTimeDs = row[20]` ajouté (col18→col20 corrigé).
- `EntryState.PendingItemUse` : record pending (item, bag, slot,
  serial, Cts, seq) à côté des PendingCube/MagicBox/Combine.
- `ZoneEntryFlow.ItemUse.cs` : `ArmItemUseChannelAsync` (407
  `{itemId, container, slot}` + `ScheduleOnLoop` à +ds/10 s),
  `FinishItemUseChannelAsync` (re-fetch slot → serial changé/item
  parti = abandon silencieux natif ; sinon `UseItemFinalAsync`),
  `CancelItemUseChannelAsync` (408 corps vide).
- `BreakCastAsync` appelle `CancelItemUseChannelAsync` en premier
  (mêlée @Combat.Mob.cs:190, SpellBreak NCID 13, mort @Death.cs:295,
  migration @Migration.cs:314, déconnexion @ZoneEntryFlow.cs).
- `ItemActionHandlers` : gate timed après toutes les validations
  (verrous, contenant, item, def, expiration, vie/mort, cooldown,
  routage item-sur-item) et avant `UseItemFinalAsync` — idem pour
  `ItemMallUseItem` (NCID 83). Double canal → silencieux natif.
- `ZoneServices.ItemUseChannel` : délégué enregistré dans le ctor
  (pattern RunOnLoopAsync des handlers stateless).

Client (`eden_godot/godot_project/scripts/`) :

- `ItemTable` : `UseTimeDs` = col20.
- `InventoryWindow` : binds 407/408 (`_pending` → main thread) ;
  407 → `CastBar.BeginCast(UseTimeDs/10f, nom FR)` + `_channelItem`
  armé ; 408 → `CastBar.CancelCast` + clear. Poll `_Process` :
  `eden_cancel_cast` (Shift+S) pendant canal → NCID 13
  `ZoneSpellBreak` (SpellBreak natif brise les 2 canaux +0x90/+0x94)
  + clear local — garde focus LineEdit/TextEdit comme SkillCast.

## Preuves

- Build serveur : 0 err / 0 warn. Build client : 0 err / 0 warn.
- Tests `ItemUseTimeTests` : 3/3 verts (col20 parse, wire 407
  `{u32,s16,s16}`, 408 vide).
- Live (client Godot + serveur natif C#, item 53 = cor, ds=50) :
  - Armement : `[Zone] UseItemTime char=200000020 item=53 bag=0
    slot=20 ds=50 — canalisation armée` ; wire `[E NCID=407]` ;
    capture `godot-shots/usetime-channel-armed.png` (barre
    « Clairon de Rêve de démon — Incantation » visible).
  - Complétion : `+5s → QuestItem enchant=50658 : node requis 117
    ≠ 5 — refus → ItemUse ... refusé par CanExecute — item
    conservé` (re-validation à l'échéance, item préservé).
  - Interruption : Shift+S en vol → wire `[D NCID=13]` →
    `[Zone] UseItemTimeBreak ... — canalisation d'usage
    interrompue` → wire `[E NCID=408]` ; capture
    `usetime-channel-cancelled.png` (barre disparue, item gardé).
  - Cooldown : `ItemUse CD ... groupe=325 actif — refus` (pas de
    réarmement pendant CD, pas de 407).

## Limites connues

- Break au mouvement : `BreakCastAsync` en MoveSelf est commenté
  côté serveur (cast mobile = règle maison — le natif brisait au
  move). Interruptions actives : mêlée, NCID 13 (Shift+S), mort,
  migration, déconnexion.
- `ClientBackItem` (614) : modèle drag natif différent — non
  applicable (cf. recon MagicBox). `ClientLifeFloat` (581) : non
  implémenté, hors scope.
