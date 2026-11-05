# Plan — statuts instantanés, état portail 55869, refresh profil

Date : 2026-11-04. Plainte : « Guérison » en status, état pierre de
portail manquant, stats profil pas toujours rafraîchies.

## Audit réalisé (constats)

Chaîne déjà correcte (vérifié, pas à retoucher) :

- `ZoneEntryFlow.Status.cs` : `AddPlayerEnchantAsync` gate
  `IsInstantOnlyEnchant` → `ExecuteInstantEnchantAsync`, aucun
  CEnchant/299 ; add/remove → `RefreshStatsAfterStatusAsync`
  (296/492/493) systématique.
- `SyncBeginnerPortalStateAsync` : 55869 `-2` visible ≤80, retiré >80,
  appelé à l'entrée (Lifecycle) et au level-up (Rewards).
- Client : `StatusKinds.Classify` (instant set == serveur, testé),
  `EnchantTable` charge c_itemmallenchant → 55869 = State → liseré
  bleu, icône I00423 présente en corpus ; `BuffBar.Displayable` filtre
  None ; 296 → `PlayerVitals.TotalStats` → `StatsChanged` →
  `ProfileWindow.RefreshStats` (abo ligne 108).
- Refresh profil couvert : équipement (`EmitSelfEquipRefreshAsync` →
  `ProfileStatEmitter`), licences 182/183 (`SendStatBlocksAsync`),
  classe (burst Services.cs:436), level-up de CLASSE
  (Rewards.cs:433), titres, AA, collections, fast-dress, strengthen,
  magic box, monture (via Add/RemovePlayerEnchantAsync).
- Natif `OnMigrationDone` : EffectToLife(-0x25c3,-2) ≤80,
  RemoveEnchantByID >80 — conforme à notre implémentation.

## Trous réels trouvés

1. **Level-up perso sans 296** — `GrantCharExpAsync`
   (Combat.Rewards.cs:833-870) : calcule vitals via `ComputeVitals`
   (équipement seul, SANS buffs) + 284, jamais 296/492/493. Natif
   `lapis::CCharacter::GainExp` appelle `RefreshEQsAttr` AVANT
   `ClientLevelUp` (désasm ~0x841d2xx). → profil figé après un
   level-up tant qu'aucun autre refresh ne part.
2. **`AddMobEnchantAsync` sans gate instant** (MobEnchants.cs:51) :
   `ApplyEnchantToMobAsync` (pont 7003) et les procs posent
   n'importe quel enchant — un enchant instantané créerait un statut
   fantôme + icône 299 sur le mob.
3. **`UseTwoWeapon` (9101) envoie un 299 factice** (Items.cs:464-471)
   — toutes les propriétés à 0, non annulable, pas de statut réel.
   Dead code en donnée FR mais même famille de bug (icône fantôme).
4. **`RestorePlayerEnchantsAsync`** : une ligne `player_enchants`
   héritée d'un enchant désormais instantané ré-exécuterait ses
   commandes (dispels !) à CHAQUE login, jamais purgée.

## Éditions

1. `ZoneEntryFlow.Combat.Rewards.cs` — `GrantCharExpAsync` :
   remplacer le bloc `eqV`+`ComputeVitals` par
   `ProfileStatEmitter.SendForCharacterAsync` (émet attrs 0x11/0x12 +
   296 + 492 + 493, retourne vitals incluant pipeline/buffs) avant le
   284, comme `RefreshEQsAttr`→`ClientLevelUp` natif.
2. `ZoneEntryFlow.MobEnchants.cs` — gate `IsInstantOnlyEnchant` en
   tête de `AddMobEnchantAsync` : exécuter les commandes via
   `SpellEffects` executor (`MobSpellEffectHost` + `SpellEffectContext`)
   puis return, sans registre ni 299.
3. `ZoneEntryFlow.Items.cs` — `UseTwoWeapon` : remplacer le 299
   factice par `AddPlayerEnchantAsync(persist:false)` (icône réelle,
   annulable, refresh stats inclus).
4. `ZoneEntryFlow.Status.cs` —
   a. `RemovePlayerEnchantAsync` : RollBack `TwoWeaponUnlocked=false`
      + attr 1000=0 si l'enchant retiré porte la commande 9101.
   b. `RestorePlayerEnchantsAsync` : purger (DeleteEnchant) les lignes
      dont l'enchant est devenu instant-only.
5. `StatusSweepTests.cs` : ajouter
   - `Assert.False(IsInstantOnlyEnchant(svc, 55869))` au test portail ;
   - `Classify(52028,…) == StatusKind.None` explicite ;
   - test mixte `[instant 1824 + stat 2001]` → pas None → statut gardé.

## Preuves

- `dotnet test eden_server/server/Eden.sln` vert.
- `flock /tmp/build.lock tools/build.sh` vert.
- In-game (à valider par le joueur) : cast 50028 sans icône ; icône
  55869 visible liseré bleu ≤80, retirée 81 ; profil mis à jour en
  direct après level-up / certificat / équipement / buff.
