# Flux des statuts (enchants) et refresh profil — audit + correctifs (2026-11-04)

Statut : code **verified** (`dotnet test` 1645 verts, build 0 warn) ;
impact in-game **hypothesis** — reste à confirmer en jeu (cast réel,
icônes, profil live).

## Chaîne vérifiée (déjà conforme avant le lot)

- Instantanés : `ZoneEntryFlow.Status.cs` — `AddPlayerEnchantAsync`
  route `IsInstantOnlyEnchant` → `ExecuteInstantEnchantAsync` : aucun
  CEnchant, aucun 299, aucune persistance. `InstantOnlyCommands`
  = miroir de `StatusKinds.InstantCommands` client (test
  `InstantCommandLists_ServerAndClientAreIdentical`). Guérison
  50028→52028 (dispel 1824 seul) classé `None` des deux côtés.
- Portail débutant : `SyncBeginnerPortalStateAsync` (Status.cs:324) —
  55869 (`S_ItemMallEnchant`, `2047|1`, durée -2) posé visible ≤ 80,
  retiré > 80 ; appelé à l'entrée (Lifecycle) et au level-up
  (Rewards.cs:817). Natif `lapis::CCharacter::OnMigrationDone` :
  `EffectToLife(-0x25c3,-2)` ; `SetLevel` : `RemoveEnchantByID` au-delà
  de 0x50 — même borne.
- Client : `StatusKinds.Classify` (commands vides ou toutes
  instantanées → None ; 2134 ride → State ; ids connus 55869/55870 →
  State) ; `EnchantTable` fusionne `c_itemmallenchant.ini` ;
  `BuffBar.Displayable` = kind≠None ∧ icône ; liseré bleu pour State
  (BuffBar.cs:615) ; icône `I00423` présente dans corpus fr-full
  (`ui/skillicon/i00423.dds`, `UI/itemicon/I00423.dds`).
- Profil live : 296→`PlayerVitals.TotalStats`→`StatsChanged`→
  `ProfileWindow.RefreshStats` (ProfileWindow.cs:108). Émissions
  serveur couvrant : équipement (`EmitSelfEquipRefreshAsync`),
  licences 182/183, changement de classe, level-up de classe, titres,
  AA, collections, fast-dress, strengthen, magic box, add/remove de
  statut (`refreshStats` par défaut).

## Correctifs du lot (verified — tests verts)

1. **Level-up perso sans 296** — `GrantCharExpAsync`
   (Combat.Rewards.cs:833+) : `ProfileStatEmitter.SendForCharacterAsync`
   remplace `ComputeVitals` — réémet attrs 0x11/0x12 + 296/492/493
   AVANT le 284, comme `RefreshEQsAttr` dans `CCharacter::GainExp`
   natif (désasm zone ~0x841d2xx : RefreshEQsAttr puis corps
   ClientLevelUp). Les vitals du 284 incluent désormais les
   contributions du pipeline (buffs MaxHP…) — avant, équipement seul.
2. **Fuite d'icône instantanée sur mob** — `AddMobEnchantAsync`
   (MobEnchants.cs:51+) : gate `IsInstantOnlyEnchant` ajouté en tête —
   les commandes s'exécutent une fois via `SpellEffects`
   (`MobSpellEffectHost`), aucun registre ni 299 (voies 7003
   `ApplyEnchantToMobAsync` et procs `ApplyProcBuffAsync`/
   `ApplyMobProcBuffAsync`, non gatées en amont).
3. **299 factice `UseTwoWeapon` (9101)** — Items.cs:452+ : remplacé
   par `AddPlayerEnchantAsync(persist:false)` (statut de session
   réel, annulable, refresh stats) + `RemovePlayerEnchantAsync`
   fait retomber `st.TwoWeaponUnlocked` + attr 1000=0 (RollBack
   `CEC_UseTwoWeapon` natif). Commande absente de la donnée FR
   (latent).
4. **Lignes persistées d'enchants devenus instantanés** —
   `RestorePlayerEnchantsAsync` les purge (DeleteEnchant) au lieu de
   rejouer leurs commandes à chaque login.

## Tests ajoutés

`StatusSweepTests.cs` : `MixedCommands_InstantPlusStat_KeepsStatus`
(mixte instant+stat → statut conservé, classé Buff) ; 52028 →
`StatusKind.None` explicite ; 55869 → non instantané + `State`.

## Correctifs du lot 2 (2026-11-04 — course de bind à l'entrée)

5. **Race BuffBridge vs burst d'entrée — CAUSE des statuts
   manquants (dont 55869)** — `BuffBridge` n'abonnait 299/300/301/
   315/372/445/446 que lorsque `HudExtensions.MountAll` créait le
   nœud, après le parse CEGUI asynchrone (`HudRoot.Vitals != null`)
   + 2 frames. Or `FinishWorldLoad` enchaîne `MountHud()` puis
   `StartLive()` (GameScene.cs:554→558) : `ZoneEntryAsync` part
   pendant que la CEGUI charge (~1 s), le serveur répond aux
   MigrationDone par les 299 (enchants restaurés, portail débutant)
   → `EdenSession.DispatchReceived` les jetait en « ncid non géré »
   (pas de handler, pas de buffer). Même famille que le bug elf
   déjà traité par `ElfBridge.EnsureBound` (commentaire
   GameScene.cs:272-276). Fix : `BuffBridge.EnsureBound` statique
   (file `Ops` d'`Action<BuffBridge>` + `_boundSession` anti-double
   abonnement, pattern identique à ElfBridge) appelé dans
   `GameScene._Ready` (chemin attaché, :280) et `StartLive` (:821)
   avant `Task.Run(ZoneEntryAsync)`. Le wiring self/cancel est
   repoussé dans `_Process` (retry jusqu'à `BuffBar.Instance`).
   Aucun changement protocole/codec.
6. **Durées ×10 trop longues (serveur)** — `AddPlayerEnchantAsync`
   prend des SECONDES (`EnchantRestSeconds` convertit les ticks
   natifs) mais plusieurs appels passaient des ticks :
   - `ZoneEntryFlow.Recommend.cs` : `def.EnchantDuration * 10` →
     `def.EnchantDuration` (le ×10 natif ciblait le param ticks de
     EffectToLife, la table est déjà en secondes) ;
   - `ZoneEntryFlow.PayEvent.cs` : `dur * 10` → `dur` ;
   - `SpellEffectHandlers.cs` (7003 arg4, ex. 58174 `…|600` = 60 s)
     : `EnchantRestSeconds((int)A(4))` — les tests
     `SpellEffectTests` attendaient déjà cette sémantique et
     échouaient avant ;
   - `ZoneEntryFlow.FastDress.cs` : `0x32` → `EnchantRestSeconds`
     (50 ticks = 5 s, pas 50 s) ;
   - `CustomClass.AngelStyleRestSec` renommé `AngelStyleRestDs`
     (0x32 ticks) + conversion à l'appel.
   Client : rien à changer — `BuffInst.Remaining` soustrait des
   secondes réelles au `RestSeconds` wire (pas de double division).

## Limites connues (mise à jour)

- Enchants de travail `200+work_num` : ids synthétiques natifs
  (`CWorkManager::Init`), absents de `c_enchant` FR →
  `BuffBar.Displayable` les masque (pas de définition) — le 299
  natif part quand même. Pas de bouche-trou : à traiter si l'icône
  d'ordre de travail est attendue en jeu.
- `ProfileStatEmitter` fige `twoWeaponLicensed:false` dans son
  snapshot (limite documentée, pré-existante).
- `ProcDef.DurationSec` (S_Enchant col 43 → `Combat.Procs.cs`) :
  unité non prouvée — la donnée montre surtout des cases vides
  (repli 60 s) et quelques `10` ; si le natif la passait en ticks à
  EffectToLife les procs durent ×10 — à trancher par désasm de la
  consommation de `CEnchantData+0x54`.
- `self_use_effect <eid> <rest>` (Admin.cs) : `rest` traité en
  secondes (sémantique documentée de la TC de dev) alors que le
  `TC_EffectToLife` natif attendait des ticks — écart assumé.
- Autres ponts HUD (`DestinyBridge`, `LoverBridge`, `HudNetBridge`)
  gardent le bind paresseux — même exposition théorique au burst
  d'entrée si le serveur émet tôt leurs NCIDs ; à traiter au
  premier symptôme.
- À vérifier en jeu : cast réel de Guérison (aucune icône), icône
  55869 liseré bleu ≤80 et retrait au level 81, icône de monture
  (2134) en état, repaint du profil après level-up/certificat/
  équipement/buff, durées réelles (recommandation, pay-event,
  7003, fast-dress ≈5 s, angel-style ≈5 s).
