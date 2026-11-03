# Rapport — portage UI CEGUI → Control Godot 4

État : **441/441 layouts parsés et convertis sans erreur**
(~12 200 nœuds UI). `dotnet test` : 32/32 verts (19 préexistants +
13 nouveaux `CeguiTests`). `dotnet build` du noyau : 0 erreur.

## Architecture

```
scripts/ui/
  CeguiImageset.cs   — .imageset → régions nommées + cache d'atlas
  CeguiScheme.cs     — .scheme → FalagardMapping + CeguiFontTable (.font)
  CeguiLookNFeel.cs  — .looknfeel Falagard → modèle MVP (109 WidgetLooks)
  CeguiLayout.cs     — .layout → arbre CeguiNode + UnifiedAreaRect → ancres
  CeguiBuilder.cs    — CeguiNode → UiNodeSpec (mapping + styles, pur C#)
  CeguiToGodot.cs    — UiNodeSpec → Control Godot (le SEUL fichier qui
                       dépend de GodotSharp — exclu de NetTests)
scripts/UiDemo.cs    — scène de démo (scenes/ui_demo.tscn)
tools/textures/copy_ui_png.py — PNG d'UI → godot_project/ui_textures/
```

Le cœur (parseurs + spec) est du C# pur, testé hors moteur ;
`CeguiToGodot` ne fait qu'instancier et poser ancres/styles/textures.

## Mapping implémenté

| Type CEGUI (TargetType du scheme) | Control Godot |
|---|---|
| FrameWindow (+ChannelWindow/BuffWindow/ChatFrameWnd) | `Panel` + barre de titre draggable + bouton × |
| Titlebar | `Panel` |
| PushButton / SystemButton | `Button` (ou `TextureButton` si NormalImage) |
| ImageButton | `TextureButton` (normal/hover/pressed/disabled) |
| Checkbox / RadioButton / ImageCheckbox / *Checkbox | `CheckBox` |
| Editbox / ComboEditbox | `LineEdit` (MaskText→Secret, MaxTextLength, ReadOnly) |
| MultiLineEditbox | `TextEdit` |
| Listbox / Listbox2 / ComboDropList / ItemListbox / MultiColumnList(2) | `ItemList` |
| Tree | `Tree` (Colonnes = nb de `ColumnHeader`) |
| ProgressBar / Alternate / Mp / Purple / Green / VUMeter | `ProgressBar` (CurrentProgress→Value 0..1) |
| Slider / HorSlider | `HSlider` ; Thumb | `Button` |
| Scrollbar Vertical/Horizontal/Large/Channel | `VScrollBar`/`HScrollBar` (d'après le nom) |
| TabControl | `TabContainer` (panes→onglets, autres enfants→conteneur `__side`) |
| TabButton | `Button` ; TabContentPane/TabButtonPane | `Control` |
| Combobox | `OptionButton` ; Menubar | `MenuBar` ; PopupMenu | `Panel` ; MenuItem | `Button` |
| Spinner | `SpinBox` ; Tooltip | `Panel` ; GroupBox | `Panel` |
| ScrollablePane (+channelScrollablePane) | `ScrollContainer` |
| Static / Border / Embed / LightStatic / zStatic | `Panel` (ou `Label` fils si Text) |
| StaticText / *TextLab / HintText / EmbedText / TipText… | `Label` (`RichTextLabel` si codes `[colour]`) |
| StaticImage / SlotImage / HintImage / channelImage / Zframe / BuffBackground / Quality* / CastingBarImage / SlotImageItem / RoundImage | `TextureRect` (stretch, région d'atlas) |
| ItemEntry (ListboxItem) | `Label` ; ListHeader→`Control`, Segment→`Button` |
| DefaultWindow / AutoWindow | `Control` |
| *ItemSlotbox* / *dragitem* / *ItemIcon* (par nom) | `TextureRect` |
| *RenderTarget* / *RenderPicture* (par nom) | `SubViewportContainer` |
| inconnu | `Control` + meta `cegui_type` + compteur |

### Types non mappés rencontrés

Sur les 441 layouts, **2 types absents du scheme** :
`DejaVu/Embed2` (×5) et `DejaVu/EmbedTextLab7` (×3). Résolus par
**préfixe** (`Embed2`→`Embed`, `EmbedTextLab7`→`EmbedTextLab`,
compteur `PrefixTypes`) → `Panel`/`Label`. Aucun type vraiment
inconnu sur l'échantillon complet (59 types distincts ; aucun
non mappé sur les 30 plus gros layouts).

## Géométrie : UnifiedAreaRect → ancres Godot

Convention : `left_anchor = x_scale`, `left_offset = x_off`,
`right_anchor = x_scale + w_scale`, `right_offset = x_off + w_off`
(idem en vertical) — strictement équivalent à
`pixel = scale*parent + offset`. Les ancres hors [0..1] sont
conservées (légal à l'exécution ; ex. `{{0,0},{1,-308},…}` de
ChannelWnd → `anchor_bottom = 2`).

Alignement CEGUI replié dans les ancres :
`Centre → left_anchor += (1-w_scale)/2, left_offset -= w_off/2` ;
`Right/Bottom → left_anchor += 1-w_scale, left_offset -= w_off`.
Cas testés : Centre avec offsets, Right, ancres >1.

## Propriétés supportées

`UnifiedAreaRect`, `HorizontalAlignment`/`VerticalAlignment`, `Text`
(codes `[colour='AARRGGBB']` → BBCode `[color=#RRGGBBAA]` ou strip),
`Visible`, `Disabled`, `AlwaysOnTop`→`TopLevel`, `Alpha`→`modulate.a`,
`Tooltip`→`tooltip_text`, `Font`→`font_size` (via `.font`, taille
réelle ; fallback suffixe numérique), `TextColours`/`TextColour`
(coin tl, ARGB→RGBA), `HorzFormatting`/`VertFormatting` (align +
word-wrap), `UnifiedMinSize`/`MaxSize` (**seule la partie offset est
exprimée** en `custom_minimum_size`/taille — la partie scale n'a pas
d'équivalent Godot), `FrameEnabled`/`BackgroundEnabled`,
`Image`/`NormalImage`/`HoverImage`/`PushedImage`/`DisabledImage`,
`Selected`, `ReadOnly`, `MaxTextLength`, `MaskText`,
`CurrentProgress`, `ColumnHeader` (nb colonnes), `GroupID`,
`MousePassThroughEnabled`→`mouse_filter=IGNORE`, `TitlebarEnabled`,
`CloseButtonEnabled`, `MultiSelect`, `ID`, `ClippedByParent`…

## Rendu visuel (MVP looknfeel)

Pour chaque type, `WidgetLook` via `FalagardMapping/@LookNFeel` :
- **Frame** : premier `FrameComponent` avec images des états
  `EnabledFrame`/`Normal`/`Enabled` → `StyleBoxTexture` NinePatch
  (image `Background` de l'atlas, marges = tailles des images de bord
  lues dans l'imageset) ; sinon `FrameColours` → `StyleBoxFlat`.
- **Background** : sections `WithFrameEnabledBackground` /
  `NoFrameEnabledBackground` → `ImageryComponent` image → Texture,
  `ColourRectProperty`/`Colours` → couleur ; sinon
  `BackgroundColours`. Coupé si `FrameEnabled/BackgroundEnabled=False`.
- **Texte** : `TextColours` du layout > `NormalTextColour` du look.
- Résolution des références croisées `Section look="DejaVu/XShared"`.

## Constructions Falagard ignorées (compteur `CeguiLookNFeel.Ignored`)

`Area` (×239), `NamedArea` (×169), `Child` (×59),
`VertFormatProperty`/`HorzFormatProperty` (×48), `TextComponent.Area`
(×18), `AnimationDefinition` (×3), plus `DimOperator`/`ImageDim`/
`WidgetDim`/`PropertyDim` à l'intérieur des Area non interprétées.

## Propriétés ignorées les plus fréquentes (compteur `IgnoredProps`)

Top sur les 441 layouts : `StepSize`, `ContentArea`, `VertExtent`,
`HorzExtent`, `Horz/VertOverlapSize`, `Horz/VertScrollPosition`,
`Horz/VertStepSize`, `ChildExtentsArea`, `DocumentSize`,
`OverlapSize`, `PageSize`, `ScrollPosition`, `ColumnsMovable`,
`TabPanePosition`, `TabHeight`, `ForceVertScrollbar`,
`ContentPaneAutoSized`, `ValidationString`, `NSSizingCursorImage`…

## Démo

`scenes/ui_demo.tscn` (Control `UiDemo` + CanvasLayer `UILayer`) :
au `_Ready`, charge `raw_assets/uidata` (chemin
`res://../raw_assets/uidata` globalisé) puis construit
`Login.layout`, `ChannelWnd.layout`, `Achievement.layout`,
`ItemDeTail.layout`, `BagManagement2.layout` en cascade.
Volontairement hors `main.tscn` : F6 sur la scène, ou l'instancier
ailleurs. Les FrameWindow sont draggables par leur barre de titre,
fermables via ×.

## Textures

`tools/textures/copy_ui_png.py` → `godot_project/ui_textures/` :
42 PNG `uidata/imagesets/`, 5 PNG `themes/Blue/`, `FONT01.ttf`
(48 fichiers ; idempotent, `--dry-run`). Les variantes
`imagesets/<Theme>/` ne sont pas copiées (collision de noms avec
`themes/`). Chargement : `GD.Load` sur `res://` puis repli
`Image.LoadFromFile` si l'import n'est pas fait.

## Limites / reste pour pixel-perfect

- Areas Falagard non évaluées (offsets d'images dans les sections,
  zone client des FrameWindow) ; tailles min/max scale ignorées.
- Dégradés par coin (ColourRect 4 coins) → seul `tl` utilisé.
- États Hover/Pushed/Disabled partiels (stylebox normal dupliqué sur
  les boutons sans images dédiées).
- Pas de binding données (ItemList/Tree/TabControl vides,
  MultiColumnList → ItemList sans colonnes visuelles).
- ScrollablePane → ScrollContainer sans synchronisation des
  scrollbars CEGUI ; RadioButton → CheckBox sans ButtonGroup.
- Thèmes recolorés (`imagesets/<Theme>/`, `themes/<T>/`) non branchés.
- Polices : taille OK, glyphes Big5 du TTF non vérifiés à l'écran
  (pas de run éditeur — imports en cours).
