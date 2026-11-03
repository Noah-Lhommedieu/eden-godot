# Plan — Classes personnalisées / « angel style » (CCData)

Sous-système natif `PlayerCCData` : 3 cartes de classe personnalisée
(type d'équipement + type d'arme + 8 sorts), une active, avec stash
d'équipement par type dans `equipment1/2.container_index` {2,3,4}.

## Preuves natives

- `CNE_CZ_ZoneServerPlayerCCDataSave::Execute` zone @0x08272bc0 :
  gates `char+0x3c&4` (combat) → texte 0x193d ; `FindEnchant(46605)`
  (AngelChangeStyleID, u16 signée -18931 @0x087b4b0c) → 0x193e ;
  `char+0x3a&1` (mort) → sortie silencieuse ; `char+0x3b&0x20`
  (riding, objet contrôlé char+0x94) → 0x193f ; `char+0x3b&0x40`
  (verrou versus/instance) + validations (slot ≤ nb cartes,
  sorts ≤ 8, equip < 4, weapon < 2, chaque spell existe dans la map
  de sorts zone+0x1b8) → corps principal :
  `RemoveAllEnchants(CCheckRemoveChangeClass)` (bit7 de
  `SEffectData+0xc` @0x0812b4a0) → `EffectToLife(46605, 0x32,
  0x2000, true, 0, 1, 0, 0)` → écrit carte `[slot]` {equip,weapon,
  sorts} (char+0xd9c, records 0x14 : +4 equip,+5 weapon,+8 vec sorts)
  → active = slot (char+0xd98) → `char+0x4ad` bit2 = weapon&1 →
  InvokePassivities/SpellStones/AAEnchants → 509 licences complètes →
  `CPlayerFactory::SwitchClassEquip(char, ancienType, nouveauType)`
  @0x080f1f90 (conteneur stash = clé `5-type`, swap slots 1..5) →
  592 `ClientPlayerClassCustomData` flag=1 {slot, equip, weapon,
  sorts} → `ModelModify(0x8000)` ×2 self + CanSee puis
  `ModelModify(0x5e00)` self + CanSee → ZMis 845 → mission.
- `CNE_ZMis_MissionServerPlayerCCDataSave::Execute` mission
  @0x08141710 → `SaveCustomClassData` : UPDATE `custom_class_data`
  (player_id, index, equip_type, weapon_type, spell_1..8) + maj du
  miroir exec+0x890 + dirty + `exec+0x88c` = index actif
  (`player_characters.current_custom_data_index`).
- `CExecutionerDBO::LoadCustomClassData` mission @0x081539xx :
  SELECT + seed INSERT (equip=1, weapon=0, sorts=0) jusqu'à 3 lignes
  ; index actif > 2 → remis à 0.
- `CNE_CZ_ZoneServerQueryClassList::Execute` @0x082767d0 : émet
  aussi le 592 flag=0 avec TOUTES les cartes (active = char+0xd98).
- Wire 242 entrant @0x08273a30 : `[u8 slot][u8 equip][u8 weapon]
  [u16 n][i32 ×n]`.
- Wire 592 @0x081fed00 : `[u8 flag][u8 slot][s16 n][u8 ×n equips]
  [s16 n][u8 ×n weapons][u16 nslots][par slot : s16 len + i32×len]`.
- `CNC_CZ_ClientModelModify` ctor @0x0813f2c0 : bit 0x8000 →
  `char+0x4ad >> 2 & 1` (bit genre CC) ; 0x5e00 = 8 champs modèle.

## Implémentation

- [ ] `CustomClassState` (Eden.Core/Game/Classes) : record carte
      {EquipType, WeaponType, int[] Spells} + constantes
      (3 cartes, stash `5-type`, enchant 46605, erreurs).
- [ ] `CustomClassRepository` : `custom_class_data` SELECT + seed 3
      lignes ; UPDATE carte ; `current_custom_data_index` get/set.
- [ ] `EntryState.CustomClassSlots` (List de 3) — `ActiveCCIndex` et
      `Vis4ad` existent déjà.
- [ ] `CustomClassPackets` (codec 592) : flag 0 (toutes) / flag 1
      (une carte) — wire prouvé ci-dessus.
- [x] `ZoneEntryFlow.CustomClass.cs` : `HandlePlayerCCDataSaveAsync`
      remplace le stub (parsage wire natif, gates, RemoveAllEnchants
      → retrait des statuts temporisés [flag bit7 non résolu :
      statuses chronométrés = transitoires], enchant 46605, écriture
      carte, licence 509, SwitchClassEquip via SwapEquipmentAsync,
      592, ModelModify 0x8000/0x5e00 self+broadcast, persistance).
- [x] `HandleQueryClassListAsync` : + 592 flag=0 toutes cartes.
- [ ] `ZoneServices.CustomClassRepo` + Program wiring + lifecycle
      (chargement cartes + index actif).
- [ ] MiscHandlers : commentaire du handler inerte 242.
- [ ] Tests : codec 592 (flag 0/1), stash key, wire 242, gates.

## Vérifs

- [ ] `dotnet build` 0 erreur 0 warning ; suite verte.
- [ ] Live : fenêtre classe perso → save → modèle + swap stuff.
