# Lot 11 — S1 : boutons "?" par champ → wiki complet (Studio)

Brief : remplacer les hints serrés font-size 10 sous chaque champ par
un bouton "?" ouvrant une fenêtre de référence complète (toutes les
valeurs + sens + exemples réels + recettes), items et monsters d'abord.

## État avant

- `IniFieldRow.Hint` affiché en FontSize 10 wrappé sous le label dans
  `ItemsView.axaml`, `MonstersView.axaml`, `IniFilesView.axaml`
  (même DataTemplate copié 3×).
- `IniSchemas.cs` = schémas (colonnes, groupes, hints) depuis
  `server/src/Eden.Core/Data/Parsers/*Parser.cs`.

## Fait — sondage données (preuves pour le contenu)

- `EquipType.ini` : kind→loc natif (20 types) — 1-6 armure, 7 1H,
  8 shield, 9 2H, 10 ranged, 11 accessoire, 13 munition.
- `MotionParser.cs` : map kind→pool d'attack native (0x11-0x13/0x3d
  blades, 0x18/0x1a axes, 0x19/0x3e fist, 0x14/0x15/0x41 2H,
  0x16/0x1b staff, 0x1c/0x3f bow, 0x1d/0x40 gun, 0x1f/0x42 cannon,
  0x20 harp, 0x21 awoken, 0x17 rapier, 0x1e scythe).
- `NativeEquipmentSlots.cs` : equipType→locs + kinds 61-68 panneau
  costume, 81 pet, 78 glyph.
- `ItemParser.cs` : formules duration unit (×60/3600/86400),
  use type dispatch, kind 22/25/45 col35, restrict flags 69/70.
- `MonsterParser.cs` : col34/35 = roam/battle speed ×0.5 (était
  "undocumented"), col50 DoD, col59 locate mask, col53/54 EXP/NP.
- `EnchantCommands.cs` : ~150 ids de commandes nommées (2134 Ride,
  2051 AttrMovement, 2048/2040 crit, 2084 dmg, 2086/2085 base…).
- Stats réelles extraites de S_Item.ini (20426 rec, 76 kinds) et
  S_Monster.ini (12896 rec).

## Implémentation

- `Services/FieldHelp.cs` : records `FieldValueCase`,
  `FieldHelpEntry` (Purpose, Values, ValuesNote, Recipe, SeeAlso).
- `Services/FieldHelpCatalog{,.Items,.Monsters}.cs` : catalogue
  statique par (file, colIndex) — toutes les colonnes mappées.
- `Services/IniColumnStats.cs` : valeurs distinctes observées +
  compteurs + exemples (id + nom EN via TranslationCatalog),
  cache par fichier+mtime.
- `ViewModels/FieldHelpViewModel.cs` : fusion catalogue × observé.
- `Views/FieldHelpWindow.axaml(.cs)` : fenêtre stylée (tokens Eden),
  instance unique réutilisée (`Present`).
- `IniFieldRow` : + TableFile/Ctx + `ShowHelpCommand` ;
  `IniFormBuilder.Build` prend `StudioContext`.
- 3 views : hint supprimé, bouton "?" (tooltip = hint).
- Vérifs : dotnet build 0/0, run.sh.

## Résultat (2026-10-24)

- [x] Hints serrés retirés des 3 vues — libellé seul + bouton « ? »
      (tooltip = hint conservé au survol).
- [x] Bouton « ? » par CHAMP (toutes colonnes, y compris « Other
      columns » non documentées → doc générique + valeurs observées).
- [x] FieldHelpWindow (640×720, tokens Eden dark, instance unique
      recyclée) : utilité, table de valeurs fusionnée doc×data,
      recette d'usage, « observed in data » (distinct + compteurs +
      exemple #id — nom EN), see also.
- [x] Wiki complet S_Item : 49 colonnes mappées documentées — table
      des kinds complète (76 kinds observés, armor/weapon/accessory/
      housing/pet/costume via EquipType.ini + MotionParser +
      NativeEquipmentSlots), use type, equip types, units 24-27,
      flags, recipes (monture = 44+use2+enchant 2134+2051).
- [x] Wiki complet S_Monster : 51 colonnes documentées (mappées +
      cols 10-12, 36, 39, 51-52, 55-58, 60-61 en « undocumented +
      observed »), col 34/35 requalifiées roam/battle speed (preuve
      MonsterParser).
- [x] Vérifs : dotnet build 0/0 ; run.sh OK ; fenêtre ouverte via
      --help=S_Item.ini:8 / S_Monster.ini:5 (captures /tmp/help_*,
      studio_sel*) ; helpcheck harness (/tmp) a validé le contenu
      end-to-end (item 20051, compteurs, noms EN).
- Nouveau flag `--help=<file>:<col>` : ouvre la fenêtre d'aide au
  démarrage (aperçu doc sans navigation).
- Reste : étendre le catalogue aux autres tables (S_Spell,
  S_Enchant…) — même mécanisme déjà générique ; tooltips des
  boutons affichent l'ancien hint.
