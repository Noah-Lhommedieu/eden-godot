# Lot S1+S4+S7 — cast skills, floaters, mort/respawn, shop, banque, loot, or

Date : 2026-10-26. Dossier : `portage/eden_godot/` uniquement.
Référence wire (lecture seule) : `eden_server/server/.../ZoneMessages.g.cs`,
`Eden.Zone/ZoneEntryFlow*.cs`, `Handlers/*.cs`, `docs/generated/FEATURE-MATRIX.md`.

## Preuves retenues (server/src/Eden.Zone + .g.cs)

C→S :
- **9** SpellCastToLife `[u16 spell][u32 cible]` — g.cs + parse serveur
  (`HandleSpellHitAsync` : `ToUInt16@0`, `ToUInt32@2`).
- **13** SpellBreak — corps vide (g.cs).
- **16** SpellCastToLocation `[u16 spell][i32 target][f32 x][f32 z]` —
  14B d'après le parse réel serveur (`f32@6`, `f32@10`) ; le .g.cs
  (short,float,float = 10B) est aplati, le handler est la preuve.
- **17/59** SpellCastToNowhere/Nearby `[u16 spell][f32 dir]` — 6B
  (`ToUInt16@0`, `ToSingle@2`).
- **35** TreasurePickup `[u32 id]` — g.cs + handler (portée 4u).
- **22/23/63/60/61/62/20** — déjà codés (`ZoneShopMessages.cs`).

S→C :
- **278** SpellCast `[s16][i32 caster][i32 cible][u8][f32 x][f32 z]` — DESASM
  (broadcast témoins, le lanceur ne le reçoit pas).
- **279** SpellCastOK `[s16 spell][i32 castDs][i32 cible][u8 ok]` — GOLDEN ;
  castDs = décisecondes.
- **281** SpellHitToLife `[s16 spell][i32 victime][i32 caster][i32 amount]
  [i32 flags]` — GOLDEN ; flags : 1 hit, 8 miss, 0x10 block, 0x200 heal,
  0x200000 crit.
- **287** SpellBreak `[i32 caster][s16 spell][u8 reason]` — DESASM 7B.
- **288** BlastOK (GOLDEN) / **289** BlastToLife (DESASM) — layouts g.cs.
- **386** TreasurePickupOK `[u32 char][u16 item][s16 n][s16]` — DESASM.
- **410** CommandFailReturn `[i32 ncid][i32 param]` — DESASM (refus cast).
- **531** ReviveInfo `[i32 area]` — GOLDEN.
- **272** existant (HitType : 1 hit, 2 crit, 3 premier, 8 miss, 0x10 block,
  0x100 parry) ; **305/306** existants ; **312/274/275/291** inventaire
  (274 header : `[u16 item][s16 bag][s16 slot][s16 count][s16 maxdur]`).

## Architecture (contraints : pas de modif HudRoot/GameScene/world/player)

- `scripts/hud/HudExtensions.cs` — nœud **autoload** (`HudExtensions` dans
  project.godot). `static Mount(Control)` → HudRoot (recherche récursive
  `is HudRoot`/nom « Hud ») sinon root Control/CanvasLayer de la scène.
  `_Process` : quand la GameScene + HudRoot monté → instancie tous les
  composants du lot (fenêtres HUD + nœuds monde) une fois. Sert aussi de
  pont réseau paresseux (pattern SessionCarry) pour les états partagés
  (sac `PlayerBag` : 312/274/275/291).
- `scripts/protocol/ZoneCombatMessages.cs` — codecs neufs ci-dessus.
- `scripts/protocol/PlayerBag.cs` — état inventaire partagé (312/274/275/291).
- `scripts/combat/SkillCast.cs` — orchestrateur cast : envoie 9/16/17,
  suit 279/287/410, expose `Instance.Cast(spellId)` ; `SpellCatalog`
  (C_Spell col1 → icône `UI/skillicon/`).
- `scripts/combat/CastBar.cs` — Control barre de cast (279→début ds/10 s,
  287/281/fin→stop ; repli 1 s sans durée).
- `scripts/combat/DamageFloaters.cs` — Node3D : 272/281 → Label3D
  billboard (montée+fade 1 s) ; blanc/jaune crit/rouge self/vert soin.
- `scripts/combat/TreasurePickup.cs` — clic droit trésor (kind 0) →
  approche ≤4u → NCID 35 ; 386 → log/chat.
- `scripts/hud/SkillBarWindow.cs` — 12 slots bas-centre (touche 1-9,0,clic)
  depuis `PlayerSkills.Spells`.
- `scripts/hud/ShopWindow.cs` — ShopWnd.layout : onglet Acheter
  (`ShopState.Entries`, or `PlayerVitals.Gold`) + Vendre (`PlayerBag`) ;
  NCID 22/23 ; fermeture → 63.
- `scripts/hud/BankWindow.cs` — StorageUI.layout : onglets conteneurs
  `StorageState` ; dbl-clic/item → dépôt 60 / retrait 61.
- `scripts/hud/GoldHud.cs` — compteur or discret + label injecté dans
  l'inventaire (`HudRoot.Inventory`).
- `scripts/hud/DeathWindow.cs` — « Vous êtes mort » + Réapparaître →
  20 (ZoneReviveRequest) ; 306 → fermeture ; events PlayerVitals.

## Validation
- `dotnet build` godot_project : 0 err/0 warn.
- `dotnet test tools/NetTests` : +tests wire (9/13/16/17/35/278/279/281/
  287/288/386/410/531 round-trip byte-exact).
- Live : `eden_server/tools/start-local.sh` + godot autopilote
  `--login=admin:adminmdp --channel --char` ; vérifs : clic droit mob →
  5/272+floaters, cast sort → 9/279+castbar, NPC → shop, captures
  `godot-shots/s1s4s7_*.png`.
