# Plan — HUD in-game fidèle au client Eden natif

Date : 25/10/2026. Référence visuelle : `../ref-shots/juste le jeu.png`.

## Layouts CEGUI natifs réutilisés (raw_assets/uidata/layouts/)

| Zone écran | Layout | Contenu |
|---|---|---|
| Haut-gauche | `MiniInfoWnd.layout` | portrait (faceFrame) + nom + HP/MP bars (HpBar/MpBar + text) + LVText |
| Bas-gauche (barres fines) | `FucntionBarWnd.layout` | expBackGrand/bar (vert) + SoulBackGrand/bar (violet) |
| Haut-centre | `limitless/LimitlessHud.layout` | FrameWindow « ULTIMATE » + Text + Bar |
| Haut-droite | `RadarUI_Ver2.layout` | RadarBody + mapImage + TitleText + CoordText + EdenClock (2 lignes) + boutons zoom/map… |
| Droite | (custom, ref Reforged) | panneau quêtes : tabs + Track/Unacquired |
| Bas-gauche | `ChannelWndV2.layout` | SubWnd/MainWnd (ChannelWindow) + InputWnd (EditBox + 9 ImageRadioButton) + MainTab + EmojiPalette |
| Bas-centre | `FunctionWnd.layout` | FastBar1/2/3 = 12 SlotImage chacune sur FunctionFastBar |
| Bas-droite | `FunctionMenu.layout` | MenuBackgrand + boutons Charact/bag/Mission/Commu/skill/City/ItemMall/Sys/Team/Work |
| Inventaire | `inventorywnd.layout` | FrameWindow + SlotContainer (grille injectée) |
| Profil | `CharacterWnd.layout` | FrameWindow + NameText/ClassText/LvText/ClssLvText |
| Centre | `ScreenMessageWnd`-style (custom) | bandeau zone jaune + texte système rouge |

## Fil réseau prouvé (serveur C# + désasm)

- Self vitals : 280 `ClientUpdateAttr` `[i32 handle][s16 attr][i32 cur][i64 max]`
  — attr 0=HP cur, 1=MP cur, 0x11=MaxHP, 0x12=MaxMP ; handle self=charId.
- 296 `AttrRefresh` : Fields15to18[1] = MaxMP (96B GOLDEN).
- 310 : `[u32 charId][str name]` (+reste brut) → nom self.
- 313 : nodeId + pos → map + minimap + bandeau.
- 600 `ClientUpdateAttrNewExp` `[i32][i64]` → XP.
- Chat S→C : 268 ClientSay `[u32][str][u32 len][blob]` ; 327 ClientChannelMsg
  `[u8 ch][str sender][str msg][u32 len][blob]` ; 266 `[u8][str]` ;
  316 `[s16][str]` ; 474 ScreenMessage `[s16][str]` (texte rouge centré).
- Chat C→S : 34 ZoneServerChannelMsg — wire réel (désasm serveur
  ZoneEntryFlow.Chat.cs) : `[u8 channel][str target][str msg][s16][s16]
  [u32 blobLen][blob]` (le .g.cs est faux sur les 8 derniers octets).
- Inventaire 312 : codec natif prouvé byte-à-byte (NativeItemCodec.cs
  @Serialize 0x8233xxx) : `[u8 nBags]{[u16 cap][u8 idx][s16 bmpLen]
  [bitmap MSB][items]}` + porte-sacs. Item record : `[u16 id][s16][s16]
  [str][u8 flags][u24 mask][cond][34B tail]`.
- Icônes items : `assets/textures/UI/itemicon/<Code>.png` (code = col1
  c_item.ini via ItemTable). Minimap : `UI/map/<code>s.png` (1024²,
  monde 576×576 → pixel x/576). Nom de zone : c_node.ini col2 (Big5).

## Composants

- `scenes/hud.tscn` : CanvasLayer + `scripts/hud/HudRoot.cs`.
- GameScene monte le HUD en solo ET live (démo hors-ligne), wire les
  NCIDs ci-dessus, relaie pos joueur → minimap, chat → envoi 34.
- Touches : I=inventaire, C=profil, Enter=focus chat, Esc=unfocus.
- Chat focus → `_player.SetPhysicsProcess(false)` (WASD suspendu).

## Tests

`tools/NetTests/ZoneHudTests.cs` : parse 312 sur corps construit selon
le codec prouvé (bitmap + mask bits), roundtrips chat 268/327/34.

## Résultat (25/10)

- [x] `scripts/protocol/ZoneHudMessages.cs` — 7 messages + parseur 312
      natif (NativeInventoryParser), tests verts (99/99 au total).
- [x] `scenes/hud.tscn` + `scripts/hud/` (10 composants).
- [x] GameScene monte le HUD en solo et live, wire 268/327/266/316/
      474/600/280/296/265/310/312/313.
- [x] Capture `../godot-shots/hud_solo.png` — panneaux présents ;
      vitals/minimap peu lisibles sur map sombre (rendu monde hors
      scope), reste du polish visuel à faire.
- [ ] `limitless/LimitlessHud.layout` retourne null au parse (commentaire
      XML ?) — UltimateBar utilise son repli ; à investiguer.
