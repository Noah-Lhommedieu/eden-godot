# Plan — Fenêtre Familier (PetWndV3) + NCIDs elf

Date : 2026-10-26 — Phase 6, lot « HUD Familier ».

## Sources de vérité (serveur, lecture seule)

- `server/src/Eden.Zone/ZoneEntryFlow.Elf*.cs` — orchestration
  (summon 109, work 107, return 108, talk 137, mix 227, rename 228,
  revive 229, pet mode 187, pet cmd 188).
- `server/src/Eden.Zone/Codecs/NativeElfCodec.cs` — layouts S→C
  prouvés (commentaires citent les adresses natives zone).
- `server/src/Eden.Zone/Handlers/ElfHandlers.cs` — dispatch C→S.
- `server/src/Eden.Core/Game/Services/ElfService.cs` — statuts
  (0 idle / 1 mix / 2 summoned / 7–9 lottery), cooldowns.
- Le serveur émet le pet invoqué via le sight mob standard (298)
  avec `entityId = -2537`, kind 12, `DisplayName = PetName`,
  `+0x48/Unk48 = OwnerCharId`.

## NCIDs

S→C : 412 EnterNodeElfInfo, 413 ElfAdd, 414 ElfReturnOK,
418 ElfUpdateAttr, 419 ElfUpdateSkill, 420 ElfUpdateSoulAbility,
424 EnterNodeElfContainer, 431 ChangeElf, 442 ElfAction,
470 PlayElfPetAction, 513 PetShortCut, 567 UpdateMixElf,
568 UpdateElfMixElf, 574 PetUpgradeCasting, 575 PetUpgradeCancel,
588 UpdateElfSpell.

C→S (uniquement acceptés serveur) : 107 ElfWork, 108 ElfReturn,
109 ElfCallElf, 137 ElfTalk, 187 PetModeChange, 188 SendPetCmd,
227 ElfMix, 228 ElfChangeName, 229 ElfRevive, 19 UseItem
(œuf kind 60, skill-book UseType 6 avec Field8 = elfLoc).
Aucun paquet « nourrir » spéculatif : non prouvé côté serveur.

## Étapes

- [x] Audit serveur + client (registry, HudRoot, SkillsWindow).
- [x] `scripts/protocol/ZoneElfMessages.cs` — messages + codecs.
- [x] `NativeItemRecord` — variante elf-container (424, cap 4,
      bitmap, sans queue fixe item standard).
- [x] `PartnerTable` (C_Partner.ini : item→modèle/icône/sorts) +
      `TextResolver.PartnerName` (t_partner.ini).
- [x] `ElfBook` — état (liste elfs, loc invoqué, mix, cast).
- [x] `ElfWindow` — PetWndV3 natif si dispo sinon fallback stylé,
      DragHelper + persist, boutons → actions C→S prouvées.
- [x] `ElfBridge` — bindings OnZone off-thread, queue main-thread,
      actions C→S, ResolveElfPet pour le registry.
- [x] `EntityRegistry` : IsElfPet / OwnerCharId / entityId −2537,
      modèle via PartnerTable.
- [x] `HudRoot` : Elf, ToggleElf, touche P, ForceOpen "elf",
      CloseAllWindows, hud-dump "Root_Pet" + "Root_PetFunctionWnd".
- [x] Build 0 erreur / 0 warning.
- [x] `dotnet test tools/NetTests` — 361/361 verts.
- [x] Live `--login=admin:adminmdp --channel --char=test3` :
      `[elf] tableau 412 : 4 familiers`, `[hud] PetWndV3.layout →
      Root_Pet ok`, `[hud] PetFunctionWnd5p.layout →
      Root_PetFunctionWnd ok`, dump `Root_Pet (463,104),(440,560)`
      + `Root_PetFunctionWnd (517,588),(265,45)`, screenshot
      `godot-shots/elf-window.png` (1366×768, région fenêtre riche).
- [x] Rapport final : NCIDs couverts, preuves, résiduel.

## Résiduel connu

- Emotes pet (470 cmds 6/7/8 → SetEventAction) : mapping
  emote→anim kf non prouvé — loggué, pas d'anim jouée.
- 420/431/442/567/568/574/575/588 : layouts parsés d'après
  NativeElfCodec ; les chemins UI associés sont minimaux
  (log + refresh) tant que le serveur ne les émet pas dans le
  flux courant.
- Sélection perso : `--char=test3` résolu par nom
  (`[auto] char select idx=2 (test3)`, self #200000004).
