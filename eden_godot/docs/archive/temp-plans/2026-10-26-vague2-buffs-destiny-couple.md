# Vague 2 — Buffs/status, Destin/Fortune, Couple/Mariage (client Godot)

> Phase 6 Vague 2. Fichiers propres au lot ; seuls `TargetFrame.cs`
> (slots buff1-4), `TopLeftVitals.cs` (FateRoadBtn natif),
> `HudExtensions.cs` (montage), `HudRoot.cs` (clés --hud-open /
> CloseAllWindows / dump), `PartyHud.cs` (buffs membres 372) et
> `SocialWindow.cs` (bouton Couple) sont partagés — éditions
> chirurgicales.

## Audit NCID (serveur `eden_server/server` — fait)

### Enchants / statuts

| NCID | Sens | Format (preuve) |
|---|---|---|
| 299 | S→C `ClientEnchantAdd` | 23 B `[i32 handle][s16 effectId][i32 restSec][u8 serial][s16 stackDisplay][i32 flagBits][s16 level][i32 counter2]` — `Status.cs` ApplyEnchant (Serialize natif @0x81e9840). Broadcast aux voyants → le client suit les buffs des AUTRES joueurs par handle. |
| 300 | S→C `ClientEnchantRemove` | 15 B `[i32 handle][s16 effectId][u8 act][i32 f4][i32 f5]` — `Status.cs` RemoveEnchant. |
| 301 | S→C `ClientEnchantUpdate` | `[i32 handle][u8 n][n×(s16 effectId][i32 casterId][i32 restSec][s16 level][s16 f4][i32 note)][i32 lifeFlags][u8 rev]` — Serialize @0x822fb70 ; émis par `DisplayChar.cs` (snapshot SeeChar) et en réponse au 18. |
| 315 | S→C `ClientEnchantReset` | `[s16][i32]` — généré seulement, jamais émis par le serveur C#. |
| 372 | S→C `ClientTeamMemberEnchant` | `[s16 serial][i32 memberCharId][u8 n][n×(s16 effect][s16 counter][s16 level][i32 restSec)]` — `Status.cs` NotifyTeamEnchantAsync (la classe générée est la version aplatie 1 entrée ; le corps réel boucle). |
| 445 | S→C `ClientEnchantPause` | `[i32][s16][u8][i32]` — écho du 127. |
| 446 | S→C `ClientEnchantStart` | `[i32][s16][u8][i32]` — écho du 128. |
| 18 | C→S `ZoneServerEnchantUpdate` | `[u32 targetHandle]` → 301. **Limite serveur** : `ZoneEntryFlow.cs` HandleEnchantUpdateAsync répond TOUJOURS un 301 vide (mobs sans enchants persistants) — utile pour vider la cible, pas pour la remplir. |
| 85 | C→S `ZoneServerEnchantCancel` | `[s16 effectId]` — abandon clic droit (`HandleEnchantCancelAsync`). |
| 127/128 | C→S pause/start | `[s16 effectId]` → échos 445/446. |

Limites serveur connues :
- Les enchants des MOBS n'ont aucun chemin d'émission (pas de map
  Statuses → pas de 299 ni 301) → les debuffs de la frame de cible ne
  se remplissent que pour les cibles JOUEURS (broadcast 299 + 301 au
  SeeChar). Documenté dans la fenêtre/rapport.
- `restSec` = secondes (path Statuses ; `SpellService` compte en ticks
  1 s — équivalent).
- `Field6` (flagBits) = 0 côté serveur → la classification buff/debuff
  vient des données : `S_Enchant col7 & 1` = « buff »
  (`EnchantService.cs:217` — bit natif du check can't-abandon).
  Col8 bit 0x10 = can't-abandon (pas de clic droit), 0x100 = no-recast.

### Destin / fortune

| 534 | S→C `ClientPlayerDestinyRoad` | `[s16 roadId][s16 roadType][s16 step][s16 rollsLeft][i32 dpPool][s16 movePoints][s16 funcState][u8 reroll][u16 n=39][n×s16 slots]` — SDestinyRoad @0x863b750 ; slots = eventIds résolus. Bloc tout-zéro = reset/fin. |
| 535 | S→C `ClientPlayerDestinyRoadEvent` | `[s16 eventId]` — déclenché à l'arrivée sur une case (op 4). |
| 589 | S→C `ClientUpdateDestinyRoad` | `[s16 type][s16 v1][s16 v2]` — types 0=step, 1=rollsLeft, 2=dpPool, 3=movePoints, 4=functionState, 5=slot[v2]=v1, 6=rerollFlag, 7=destinyVit. |
| 590 | S→C `ClientDestinyDiceFailed` | `[s16 op]` — refus d'op (scène/mort/versus). |
| 557 | S→C `ClientUpdateFortuneBagCounter` | `[s16 n][n×(s16 bagId][s16 count)]`. |
| 203 | C→S `ZoneServerDestinyDice` | `[s16 op][s16 p2][s16 p3]` — ops : 0 créer (vit≥100), 1 lancer (dé→move 1-6), 2 item (p2=itemId p3=slot), 3 fonction/choix (p2 : bit f0-15 ou face dé si reroll), 4 avancer (step+=move, événement de case). |
| 204 | C→S `ZoneServerQueryDestinyRoad` | vide → 534 si route active (sinon rien). |
| 223 | C→S `QueryFortuneBagCounter` | vide → 557. |

- `destiny_vit` n'est émis que via 589(7) (regen 72 s / création /
  item vit) — à l'entrée la valeur DB n'est pas poussée → la fenêtre
  affiche « ? » tant qu'aucun 589(7) n'arrive (amélioration serveur
  possible hors périmètre).
- Pas de paquet de résultat « tirage fortune » → pas de FortuneWindow
  dédiée ; les compteurs 557 sont affichés dans la fenêtre destin.

### Couple / mariage

| 475 | S→C `ClientLoverInvite` | `[i32 charId][str name][s16]` |
| 476 | S→C `ClientLoverUpdate` | `[s16][i32][str]` |
| 477 | S→C `ClientLoverInfo` | 14 champs `[i32][i32][s16][str][str][i32][i32][s16×4][str][s16][u8]` |
| 478 | S→C `ClientLoverBreak` | `[i32]` |
| 479 | S→C `ClientLoverPosition` | `[i32][s16][f32][f32]` |
| 597 | S→C `ClientMarryOperation` | `[s16]` — émis au succès du 249 op1. |
| 599 | S→C `ClientWeddingsInfo` | `[u8 n][n×(i32 loveId][str n1][str n2][u8 status][i32 leftTime)]` — WeddingPackets.cs (classe générée aplatie incorrecte) ; liste vide sans mission. |
| 154 | C→S `ZoneServerLoverInvite` | `[u32 charId][str name][s16]` — propose/accepte (mutuel → couple). |
| 155 | C→S `ZoneServerLoverMessage` | `[str]` |
| 156 | C→S `ZoneServerQueryLoverInfo` | vide |
| 157 | C→S `ZoneServerLoverBreak` | vide |
| 249 | C→S `ZoneServerMarryOperation` | `[s16 op][s16]` — op1 = demande de mariage. |
| 251 | C→S `ZoneServerWeddingInfoQuery` | vide → 599. |

- `SocialHandlers.cs` : les handlers 154/155/156/157 existent mais les
  réponses 475-479 ne sont pas émises par le serveur C# actuel → la
  fenêtre couple affiche l'état local (invites reçues/données) et la
  doc liste la lacune. 597/599 sont les seuls chemins émis prouvés.

## Données client (raw_assets — vérifié)

- `c_enchant.ini` |V.3|61| : col0 id, col1 code icône
  (`E0302`,`J515`,`ERLR1`… → `UI/skillicon/<code>.png`, repli
  itemicon/uiicon — fichiers vérifiés), col5 nom Big5,
  col7 bit0 = buff, col8 bits 0x10/0x100, col56 desc Big5.
- `T_Enchant.ini` : col1 = nom EN, col3 = desc EN (15 064 lignes) ;
  col2 tagline partielle (4 470). → `EnchantTable`.
- `C_DestinyRoad.ini` |V.2|42| : col0 id, col1 tier, col2 backdrop,
  cols3-41 = 39 groupes d'événements — `DestinyDataParser` serveur.
- `C_DestinyEvent.ini` |V.2|8| : col0 id, col1 group, col2 code
  (fate01→`UI/uiicon`, S81016xx→itemicon), col5 type (0 rien,1 or,
  2 exp,3 NP+CP,4/6 mob,5 enchant,7 dés), col6 value, col7 param2.
- `t_destinyevent.ini` : col1 = nom EN (+desc multi-lignes).
- Layouts : `BuffWnd.layout` (DéjaVu/Static + BuffBackground + 15
  SlotImage Buff1-15 avec Text bas-droite — construit tel quel),
  `TargetInfoWnd.layout` buff1-4 (StaticImage+Text),
  `FateRoadWnd.layout` (FrameWindow 272×266, 39 RoadIcon + StepNow +
  UseBtn + Text + CloseBtn), `CommunityWnd.layout` LoverTab (référence
  structurelle), `Wedding.layout` (référence).
- `MiniInfoWnd.layout` : `FateRoadBtn` (Visible=False natif, montré
  par code) → point d'entrée natif de la fenêtre destin.

## Implémentation

### Nouveaux fichiers

- [x] `scripts/protocol/ZoneEnchantMessages.cs` — codecs 299/300/301/
      315/372/445/446 + C→S 18/85/127/128 + store `EntityBuffs`
      (handle → liste, countdown côté client, event Changed).
- [x] `scripts/protocol/ZoneDestinyMessages.cs` — codecs 534/535/589/
      590/557 + C→S 203/204/223 + `DestinyState` + `FortuneCounters`.
- [x] `scripts/protocol/ZoneLoverMessages.cs` — codecs 475-479/597/599
      + C→S 154/155/156/157/249/251 + `LoverState`.
- [x] `scripts/data/tables/EnchantTable.cs` — c_enchant + T_Enchant :
      icône code, nom EN, desc EN, IsBuff (col7&1), NoAbandon (col8
      0x10).
- [x] `scripts/data/tables/DestinyTable.cs` — C_DestinyRoad +
      C_DestinyEvent + t_destinyevent : route (tier, backdrop, cells),
      event (group, code icône, type, value, param2, nom EN).
- [x] `scripts/hud/BuffBar.cs` — `BuffWnd.layout` natif : Buff1-10 =
      buffs, Buff11-15 = debuffs (liseré rouge), icône skillicon
      (repli itemicon/uiicon), Text = « m:ss » + « ×N », tooltip
      nom+desc+reste, clic droit → 85 si NoAbandon == false.
- [x] `scripts/hud/BuffBridge.cs` — Node : binds 299/300/301/315/372/
      445/446 → EntityBuffs → BuffBar (self = SelectedCharId) +
      TargetFrame (handle courant) + PartyHud ; poll changement de
      cible → envoi 18 ; sender 85.
- [x] `scripts/hud/DestinyWindow.cs` + `DestinyBridge.cs` —
      `FateRoadWnd.layout` EST la fenêtre (39 RoadIcon = eventId →
      icône + tooltip nom EN/type ; StepNow repositionné ; UseBtn =
      lancer ; titlebar native = drag) + bandeau ajouté : Vit, DP,
      dés, move ; boutons créer/lancer/avancer/fonctions f0-5/choix
      reroll ; banner événement 535 ; échec 590 ; compteurs fortune
      557 + bouton 223. Auto 204 à l'ouverture + au bind.
- [x] `scripts/hud/CoupleWindow.cs` + `LoverBridge.cs` — panneau
      « Couple » (structure LoverTab : partenaire, statut, date,
      actions propose/message/break/marry/weddings, liste 599 ;
      popup d'invite 475 ; skills couple en slots grisés — la 477
      n'étant pas émise, afficher ce qui arrive).
- [x] `tools/NetTests/ZoneVague2Tests.cs` — 16 tests : round-trips +
      stores (EntityBuffs countdown/pause, DestinyState 589
      atomiques, LoverState cycle invite→info→break).

### Éditions chirurgicales partagées

- [x] `TargetFrame.cs` — `SetBuffs(List<BuffInst>)` sur buff1-4
      (icône+tooltip, teinte rouge debuff), Clear() les vide ;
      + Instance/TargetKind/TargetName/TargetIsPlayer/TargetCharId.
- [x] `TopLeftVitals.cs` — FateRoadBtn : Visible=true + Pressed →
      DestinyWindow.Instance.Toggle().
- [x] `PartyHud.cs` — icônes buff par membre (EntityBuffs par
      memberCharId, alimenté par 372) + subscribe Changed.
- [x] `SocialWindow.cs` — bouton « ♥ » → toggle CoupleWindow.
- [x] `HudExtensions.cs` — MountAll : Mount BuffBar/DestinyWindow/
      CoupleWindow + AddChild BuffBridge/DestinyBridge/LoverBridge.
- [x] `HudRoot.cs` — ForceOpen : clés `buff(s)`/`buffbar`,
      `destiny`/`fateroad`, `couple`/`lover`/`wedding` (démos hors
      session) ; DumpRects : + Root_BuffWnd/DestinyPanel/CouplePanel/
      LoverInvitePopup.

## Validation

- [x] `dotnet build godot_project/EdenClientReborn.csproj` — 0 erreur,
      0 warning.
- [x] `dotnet test tools/NetTests` — 410/410 vert (16 nouveaux tests
      Vague 2).
- [x] Live `--login=admin:adminmdp --channel --char --hud-open=
      buff,destiny,couple --hud-dump` — binds posés
      (self=200000002), 3 fenêtres ouvertes, 0 dispatch error ;
      screenshots `godot-shots/vague2-hud.png` (panneaux visibles :
      icônes buffs démo saturées dans Buff1-3, plateau destin +
      bandeau, fenêtre couple + popup invite) et
      `godot-shots/buff-bar.png`.
- [x] Wire live : 301 reçu pour self 0x0bebc202 (=200000002) + 3×
      299 (effects 0xda3d, 0xd6d7 rest=100s) + 557 (n=0) — parsés
      sans erreur (arrivés avant le bind → store non rempli,
      comportement attendu).
- [x] Rapport : NCIDs, preuves, lacunes serveur (18 vide, mobs sans
      enchants, 475-479 jamais émis par le serveur C# actuel,
      destiny_vit non poussée à l'entrée).

Pas de commit.
