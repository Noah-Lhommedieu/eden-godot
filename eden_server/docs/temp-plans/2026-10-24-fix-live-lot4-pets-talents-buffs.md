# 2026-10-24 — Lot live 4 : pets / talents / buffs / AoE / buyback / rf_game

Bugs remontés au retest live après le lot 3 (`28d8a20`, zone :5201,
1073 tests verts). Preuves natives citées par binaire@adresse.

## 1. Pets

### Toggle passif/combat — NCID 187 `CNE_CZ_ZoneServerPetModeChange`
Natif `Execute @0x082700f0` (zone) : payload `[s16 mode @+0x12]`.
- elf pet = `char+0x7dc+4`, vtype 0x50b, `CKFElf::GetHp > 1` requis
  (sinon log 0xb86, rien d'autre).
- `char+0x7e8 == 0` → mode forcé à 2.
- `pet+0x3ec = mode` ; mode==2 → texte **0xb30** au maître +
  `UnhookAllFightingRelation` + `pet+0xd0=0` ; sinon texte **0xb31** +
  `BecomeFightingCandicate(pet, char+0xd0)`.
- `CKFElf::SetAttackState(elf,1, mode!=2)` @0x08456760 : bit 0x1 de
  `elf+0x3a`, persisté via DBA MissionServer (« Update Elf Status »).
- [x] Port : `HandlePetModeChangeAsync` (Elf.cs ~765) → + texte
  0xb30/0xb31 (TextOutputIndex), clear cible quand mode==2,
  assiste sinon (TickPetsAsync gère déjà la sélection de cible).

### NCID 188 `CNE_CZ_ZoneServerSendPetCmd` — payload `[s16 instr][i32 targetId]`
Natif `Execute @0x08285e30` → `CElfPet::DoAction @0x08458270` :
- targetId résolu par `GetLifeEntity(node)` ; si trouvé → pet face à
  la cible + **rebroadcast du SeeMob du pet** au CanSee.
- instr 1 → vfunc +0x1dc(pet, 100, target) = `SetPetInstruction(100)`
  @0x084588e0 : pet+0xd0=cible, mode 2→1 (+texte 0xb31).
- instr 6/7/8 → emotes (CAI::SetEventAction 0x1fa/0x1f6 / LifeEntityDo).
- instrs 101-105 → `CastSpell` battle_spell[idx] sur la cible.
- `SetPetInstruction` : instr 0/1/2 = mode direct ; 100 = lock cible
  (+mode 1 si passif, texte 0xb31) ; 0x65 = release (+mode 2).
- [x] Port : relire `[s16][i32]`, instr 1/100 → cible directe + mode 1,
  101-105 → cast du battle spell si cible, relais 470 conservé pour
  les emotes.

### Pet « sous la map »
- **z=0 est le comportement natif exact** : `BornElfPet @0x084b5d30`,
  `BornPet`, `BornTotemPet`, `BornMonster`, `BornNPC` écrivent tous
  entité+0x30=0 ; `Serialize` du 298 écrit 4 floats (+0xc..+0x18) dont
  ce z — le client dérive la hauteur terrain lui-même (326 ne porte
  que x/z au sol). Les Z de scène des mobs sont aussi à 0.
- [ ] Suspects restants (validation in-game requise, z=0 prouvé natif) : position (0,0) au spawn si PosX/PosZ pas
  encore chargés ; ordre des floats ; modèle -2537 côté client.
  → validation in-game requise avant de toucher le wire.

## 2. Talents de classe (icônes + stats)

### Icônes 299 fantômes
- `RestorePlayerEnchantsAsync` restaure les lignes `player_enchants`
  d'effect_id advanced_ability (persistées par un ancien bug) SANS flag
  hidden → icônes.
- [x] Skip + DELETE des effect_id AA au restore (purge DB).

### Commandes non implémentées (LicenseService.ApplyEnchantCommands)
Preuves dispatch natif `CSpellDBO::DecodeEffectCommand` + CEC :
- **2040** : `zone@0x838cfb6` (cmp edx,2040) → "AttrCriticalRate 2" +
  "AttrMagicCriticalRate 2" → crit **%** (accumulateurs +0x104/+0x108,
  distincts du flat +0x100 de 2048).
- **2043** : `zone@0x8384b38` cmp edx,0x7fb → `0x8391176` émet
  "AttrCriticalDamage 0" + "AttrMagicCriticalDamage 0" → **stats
  joueur directes**, PAS per-spell. La table EnchantCommands[2043] est
  fausse (le per-spell magic crit dmg est **2099**).
- **2064** `AttrSpellDamageRate` : `Init @0x08511a00` — 1er arg =
  valeur, suivants = spellIds ; `Execute @0x08511990` →
  `CCharacter::AddSpellDamageRate(spell,val)` ; `CCharacter::SpellDamage`
  applique `(100+mod)*dmg/100`. → SpellAttrMods nouveau kind.
- **2069** `AttrCastTimePlus 2` : `Execute @0x085014b0` — mode 2 →
  float dans +0x13c (accumulateur % du taux de cast).
- **2059** = `AttrCastFailRate` (pas cast time).
- [x] Corriger EnchantCommands[2043], implémenter 2040/2043/2064/2069,
  router les AttrSpell* talents vers SpellAttrMods au login.

## 3. Buffs de skills (ni icône ni effet)

- `ExecuteSpellOnLifeAsync` gate `def.Value > 0` exclut les buffs à
  durée vide (col41 = '', ex. Protective Light 50056). Natif
  `TC_TestSpellAttack @0x083c7750` : `EffectToLife(target, caster,
  resultEnchant, SpellDuration=SSpellData+0x84 [défaut -9], ...)`.
  -9 = « durée propre de l'enchant » (SEffectData+0x110) — **et**
  `ILifeEntity::AddEnchant` n'émet le 299 que si rest ≠ -9... vérifier
  le gate exact du 299 natif.
- `ApplyNowhereResultAsync` (Resolution.cs ~976) = dégâts AoE
  seulement : un buff self via NCID 17 n'applique jamais l'enchant.
- [x] Gate sur la durée propre de l'enchant / -9 natif, pas Value>0.
- [x] Appliquer le résultat enchant au caster dans le chemin 17.
- [x] Vérifier émission 299 + persistence + stat refresh.

## 4. AoE multi-hit toujours KO
- [x] Collecte corrigée (shape 0 rayon≥1 centré cible, résultat par victime, PBAoE nowhere natif autour du lanceur) — **reste** : cap de cibles ? PvP/équipe non couvert (scan mobs only), validation in-game requise (rayon col30×0.5, formes 2/5/6),
  cap de cibles natif, NCID réel de Reforged (16 ToLocation vs 9
  ToLife), émission 281 par victime. Preuve client requise.

## 5. Buyback (packet 512)
- [x] Reverser le layout natif (SellItems::Execute remplit la liste de
  rachat ; NCID 512 émis après vente). Pas de speculation wire.

## 6. Doc terminologie
- [x] « enchant » = stat/proc posé sur équipement ; « buff » = gain de
  stats temporaire (proc ou skill). Clarifier
  `docs/implementation/11-enchanting-system.md` + docs liées.

## 7. rf_game (client Reforged)
- [~] Caméra clic-droit bloquée pendant timed cast : cartographié
  (docs/PATCH-CLIENT-BINAIRE.md § État) — le yaw n'est gâté que par
  le lock-marche `ctrl+0x2e4 bit1`, PAS par le cast ; le setter
  RMB-drag 0x75a1b0 est inconditionnel → le clic est probablement
  avalé en amont par le dispatcher. Reste : breakpoint x64dbg live.
- [~] Strafe seul sous monture ne pivote pas le skin (90°) : patch
  posé — `0x75DC08 EB 0D→90 90` (le `jmp` qui sautait
  `FUN_0069a990(cam,10)` = directive de facing monture vanilla est
  neutralisé ; SetDir + directive pour tout départ de déplacement).
  **RETEST JOUEUR requis.**
- Patchs existants : game_.exe @0x75dbd6 NOP, 0x75AC17/0x75DCB2
  restaurés. Headless ne peut PAS valider ces points.

## 8. Validation
- [x] `dotnet build` 0 erreur + `dotnet test` 1073+ verts.
- [ ] Deploy zone :5201 (build Release + restart) + retest joueur (pets/talents/buffs/AoE).
