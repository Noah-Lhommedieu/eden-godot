# Audit panels : langue + thème (A5.8)

Date 2026-10-30. Légende : OK = conforme, CORRIGÉ = modifié dans ce lot, RESTE = à faire.

## Mécanismes centraux
- Titres : `DragHelper.EnsureTitleBar` passe le titre par `UiTextEn.Translate` (tous les `MakeWindow` en profitent) : OK.
- Chrome : toutes les fenêtres listées passent par `EeSkin.MakeWindow` / `EnsureTitleBar` (cadre natif + `ApplyThemePalette`) : OK.
- `ResolveRawAsset(...t_...)` : seul cas = `NpcDialogueWindow.DialogueText` (repli après `TextResolver.ResolveFile`, donc langue respectée) : OK. Aucun autre contournement trouvé.
- TSV `ui_text_en_fr.tsv` : doublons `Mount`/`Reset` (fin de fichier) supprimés ; 0 doublon de clé ; 16 clés ajoutées.

## Checklist fenêtres (MakeWindow / EnsureTitleBar)
| Fenêtre | Thème | Langue |
|---|---|---|
| SystemMenuWindow | CORRIGÉ (InsetBox thémée) | OK ("Reset UI" via TSV) |
| ExpertiseWindows | CORRIGÉ (InsetCard thémée) | OK |
| AdvancedStatsWindow | CORRIGÉ (InsetCard thémée) | OK |
| AchievementWindow | OK (cartes sombres + texte clair, lisible) | OK |
| QuestWindow | OK (arbre clair + encre sombre, contraste ok) | OK |
| ArchiveWindow, CostumeWindow, ItemPreviewWindow | OK | OK |
| DailyRewardWindow | OK | CORRIGÉ (titre) |
| MailWindow | OK | CORRIGÉ (placeholder, hint, statut, "De :") ; textes --mail-drive = debug interne (non joueur) |
| LotteryWindow, HotelWindow, MessageBoardWindow, BankWindow, FamilyStorageWindow, CraftWindow, AuctionWindow | OK | CORRIGÉ (libellés principaux ; Craft : titre, ordres, statuts) |
| GuildWindow | OK | CORRIGÉ ("Journal" ; les ~15 infobulles "op 0x.." remplacées par des infobulles joueur traduites) |
| LoadingScreen, BootScreen, SettingsWindow ("… touche ?") | OK | CORRIGÉ |
| InventoryWindow, ProfileWindow, ClassWindow, ShopWindow, ItemMallWindow, NeobankWindow, VendingWindow, TradeWindow, SocialWindow, ElfWindow, DestinyWindow, CoupleWindow, AltarWindow, BattlefieldWindow, RegionMapWindow, WorldMapWindow, NpcDialogueWindow, FeatureDialogWindow, DuelDialog, RollTreasureDialog, SystemServiceWindows, EdenWindow, EquipSlotsPanel, TopLeftVitals | OK (chrome natif) | OK pour l'essentiel (ShopWindow " g" → "po" via TSV) ; RESTE : "Region Map"/"City Features"/"Class"/"Main Menu"/"Character Info" en EN en dur (titres traduits via EnsureTitleBar) |

## Fait dans ce lot (2e passage)
- Mail "De :", Achievement "… N autres", Craft (titre/ordres/statuts), Guild (infobulles), Shop (" g") : `UiTextEn.Translate` + 30 clés TSV (0 doublon).
- ClassWindow.cs(1142) : fonction locale `Pts` inutilisée supprimée (warning à zéro).
- A5.7 LoadingScreen : positions mesurées sur le ref-shot (contenu 1280x720) en ancres fractionnaires : titre (8,66), sous-titre (8,103), lore (16,477) 490 px, tip haut-droite y=14, barre collée coin bas-droit (texture 600x150, bbox 11..594).
- A4.3 BuffBar/NerfBar : entête de drag (bande 12 px) via `HudLayout` (clés `BuffBar/...`, `NerfBar/...`), redim. = colonnes (grip), `ScrollContainer` interne au-delà de 6 rangées, fenêtre masquée si vide.
- A6.7 ItemPreviewWindow : catalogue cliquable (montures, teintures, star stones, luna rocks, météores — noms EN de t_item), bouton "Item preview" dans le Main Menu, lien item→monture (UseEnchantId → enchant 2134|rideKey, `EnchantTable.RideKeyOf`), corps + catalogue construits en tâche de fond.

## Reste
- Interpolations FR : ArchiveWindow déjà via Translate ; titres EN en dur ("Region Map"/"City Features"/"Class"/"Main Menu"/"Character Info") traduits par EnsureTitleBar.
- Teintures : aucun mapping item-teinture → entrée DYE_* de C_CharColor côté client ; cliquer une teinture affiche sa description, pas de teinte sur le mannequin.
- Preuve visuelle FR/EN par panneau, BuffBar (drag entête, scroll), ItemPreview et LoadingScreen : non faite (Godot non lancé).
- Build/NetTests bloqués au moment du rapport par `scripts/data/LegendaryWikiData.cs` (fichier d'un autre agent, erreurs de syntaxe) ; mes changements compilent 0 erreur 0 warning dans une copie sans ce fichier.

## A2.1 Settings : bascule native et contenu (2026-10-30)

### Bascule `OptionControlUIVer2.layout` : NON retenue
Le pipeline CEGUI (`CeguiBuilder`) mappe bien TabControl/Checkbox/RadioButton/Scrollbar/Listbox/Combobox, mais la fenêtre native est inexploitable telle quelle (client non lancé : analyse du layout, pas de rendu testé) :
- ~44 `StaticTextLab` et boutons portent des textes zh-TW en dur (最佳化, 人物顯示, 滑鼠速度…) ; l'onglet Preferences natif est une suite de cases dont le texte est un index brut (`"17(RejectCoRide)"`, `"19 UIeffect"`, `"24 ClickToMove"`, `"26"`, `"27"`), donc non traduisible ni alignable sur la ref EN (Name/Guild/Title…).
- Les « sliders » sont des `HorizontalScrollbar` (17) + `Listbox/MultiColumnList` internes d'une zone `List` à défilement propre, les groupes Low/High/Off/Char/All sont 33 `RadioButton` à `GroupID` : il faudrait ~100 bindings nommés vers `ClientSettings`/`HudPrefs`, aucun vérifiable sans lancer Godot (consigne).
- Hotkeys = `MultiColumnList` : pas de capture de touche par ligne dans le pipeline (SocialWindow aplatit déjà les MCL en `ItemList` une colonne).
- Pas de thème ni de contraste pilotable : `ApplyContrast`/`ThemePalette` ne s'appliquent qu'aux contrôles hand-built.
Décision : fenêtre hand-built (`SettingsWindow`, `EeSkin.PanelTheme`) conservée, contenu aligné sur le natif. À retenter quand un run Godot pourra valider un rendu FrameWindow+TabControl.

### Video (effets réels)
| Réglage | Effet |
|---|---|
| Models Low/High | `Viewport.MeshLodThreshold` 4/1 + `EntityAnimator.AnimLodScale` 0.4/1 (gel d'animation plus proche) |
| Textures | `Viewport.AnisotropicFilteringLevel` off/4x/16x (résolution des assets : non rechargeable à chaud) |
| Field | `Camera3D.Far` (ex « View distance ») |
| Char Draw (5-100) | plafond de joueurs distants dessinés (les plus proches), `Visible=false` + PickArea coupée |
| Max FX (0-60) | plafond de `SpellFx` d'autrui simultanés (les siens passent toujours) |
| Background (5-60) | `Engine.MaxFps` borné quand `DisplayServer.WindowIsFocused()==false` (« 背景張數 ») |
| Outline Off/Character/All | overlay inverted-hull (`MaterialOverlay`, shader `POSITION` en espace monde, 0.022 u) sur le Visual des joueurs / de toutes les entités ≤ 60 u ; pas les props de carte |
| Death | désaturation écran (Environment adjustment 0.2) tant que `PlayerVitals.IsDead` |
| Blur | = Depth of field existant |
| Water Wave | uniform `wave`+`fps` du shader eau (ondulation + caustiques) |
| 3D Effect | coupe `SpellFx.Spawn` |
| Photo | étalonnage contraste 1.08 / saturation 1.15 (hypothèse : sens natif non documenté) |
| Glare | glow renforcé (intensité ×1.5, bloom ≥ 0.12) même Bloom off |
Shadows spécial = case Shadows existante. Groundcover reste une case (pas de densité).

### Hotkeys
Ajoutés : `eden_party_01..10` (F1-F10 = cible le membre du roster `PartyState` visible, 124), `eden_sit` (X), `eden_weapon` (Z), `eden_attack` (R). Community (O) / Pet (N) existaient (`eden_social`/`eden_pet`), libellé « Community ».
- Sit/Stand : local seulement (anim `sit01` tenue comme idle, relevé au mouvement/saut/attaque). Le serveur n'a aucun message d'assise → les autres joueurs ne le voient pas.
- Toggle WPN : cache les BoneAttachment `__equip_ba_Prop*` du corps local (local seulement).
- Attack : engage le mob ciblé, sinon le plus proche ≤ 12 u (hors familiers).

### Preferences
Implémentés (persistés dans `clientSettings.json`, remis à zéro par Reset preferences) : Mouse (sensibilité orbite caméra), Guild, Title, Vitals, Self-Cast, Critical Shock, Vibration, Jelly Effect, Camera Follow, Left-click to route (+ déjà : Name, NPC Name, Damage, barres, Invert Camera, Refuse Trade/Duel/Party).
- Guild : soi seulement (265/263 ne portent pas la guilde des autres). Title : soi (397) + joueurs vus (396, `PlayerTitles.Others`).
- Vitals : texte HP/MP des barres TopLeftVitals. Self-Cast : sort de soutien (type C_Spell 2-5) sans cible → cible = soi. Critical Shock : secousse caméra 0.25 s sur critique subi ou infligé à la cible courante ; Vibration : `Input.StartJoyVibration` si manette. Jelly Effect : rebond élastique à l'ouverture de toute fenêtre `HudWindows`. Camera Follow : le yaw caméra se recale vers le cap de marche (pas en marche arrière).
Non implémentés (aucun toggle mort) :
- Guild Insignia : aucun asset d'emblème côté entités, pas de donnée guilde distante.
- Decline Tandem : les invitations co-monture (`ZoneClientCoRideInvite`) sont décodées mais aucun UI/handler ne les consomme.
- Moving Mouse : le glisser gauche pilote déjà la caméra, pas d'équivalent « marche vers la souris ».
- Search denied : flag serveur (recherche de joueur), pas de message client connu.
- Jelly (sens exact natif inconnu : interprété comme effet UI d'après l'index « 19 UIeffect »).
- Battle Recorder, Event Effect, PVP War Armor, Recommended Equ… : aucun système client correspondant (enregistreur, évènements saisonniers, armure de guerre, suggestion d'équipement).
