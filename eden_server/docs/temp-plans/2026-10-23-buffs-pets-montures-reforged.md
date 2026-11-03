# Plan 2026-10-23 — Buffs/statuts, pets en zone, montures, cast Reforged

Retour utilisateur après tests Reforged. **Périmètre : client Reforged
uniquement** à partir de maintenant.

## 1. Pets — « Pets unavailable in this zone! » (8350) — ✅ FAIT

- **Preuve** : `ZoneServerElfWork` natif (NCID 107) refuse quand le
  bitmask de scène (`node+0x28→+0x10`) n'a pas le bit pet (`0x20/0x40`).
  Notre gate lit `S_Node.PetFlag` — le parseur lisait `col 42` = floats,
  toujours 0 → refus partout.
- **Fait** : `GameDataParsers.cs` — index ancré au code `Sxxx` + 42
  (le natif consomme 4 ints par `strtol` manuel avant le bloc ushort ;
  la queue est à longueur variable par paires). `PetFlag` réel =
  `{3:329, 2:14, '':36}` — cohérent. Commit `b441b36`.
- **Validé headless** (`/tmp/eden_elf_probe.py`, ticket réel → zone) :
  ElfWork NCID 107 `[s16 loc][s16 mode=2]` → réponses **513**
  (ElfWork result) + **418** (ClientElfUpdateAttr status→2 invoqué) —
  sémantique `CKFElfManager::CallElf` @0x8072c30 (SetElfStatus 2 +
  notif result + UpdateAttr). À confirmer visuellement en client.

## 2. Statuts/buffs — les stats ne bougent pas — ✅ FAIT

- **Preuve** : buffs appliqués (logs `spell-result cmd enchant=52026`)
  mais `LicenseService.ApplyEnchantCommands` ne traduisait que HP/MP/
  stats primaires.
- **Layout natif résolu** (`LoadAllEffectData` @ 0x08394b10) :
  `SEffectData+8` = col7 → bit0 = is-buff (annulable) ;
  `SEffectData+0xc` = col8 → bit 0x10 = can't-abandon.
- **Cancel natif** : `CancelPlayerEnchant` @0x0805fe00 — NCID 85
  `[s16 effectId]` ; refuse si `col7&1==0` ou `col8&0x10!=0`. Vrai
  routage vers `RemovePlayerEnchantAsync` + 300 + delete DB. Commit
  `a0dfbae`.
- **Counter 299** : `CEnchant+0x30` incrémenté par add natif → Field5
  émis (`d7662c6`). Golden `ncid_299.json` = 1 au premier add.
- **Canaux stats** (désasm `DecodeEffectCommand` + CEC_*::Execute) :
  `StatContribution` étendu (DEF/RES/AtSpd/Hit/Dodge/Crit/CritDmg/
  Move/Block/…), formules natives branchées (`CCharFormula::Str` =
  `(eq+base)×(pct)×0.01 + post` ; AttrBase mode0→post, mode1→
  spellref[col18-25]×var×0.01, mode2→pct). Commandes corrigées :
  2081=AttrBase flat (pas AtkSpd), 2082=DEF/Hit/Dodge/PAtk/MAtk,
  2083/2085/2086/2087 canaux %/post, 2095=resist elem 4-9,
  2101=resist elem 1-3, 2048/2056/2117 crit/block, 2051 mouvement.
  Tests 1073/1073.

## 3. Monture — strafe sans rotation du corps — ✅ côté serveur (à retester)

- **Preuve** : le u16@0xe du 326 n'est PAS la vitesse — c'est le
  facing en half-float (`char+0x58`), bit0 forcé 0/hasDst
  (`MoveSelf` natif @0x0829a390 ; golden 326 : `face=0x43da` ≈ 3.96 rad).
  `CNC_CZ_ClientMovePrediction` reconstruit : facing passé partout,
  mobs = atan2(dx,dz). Commit `094140a`.
- NCID 123 turn : `st.Facing` mis à jour + broadcast 441 TurnChar à
  l'acteur ET aux observateurs (`BroadCastToCanSee` inclut l'acteur).
- **Cycle mount/dismount validé headless** (`/tmp/eden_dismount_probe.py`)
  : ItemUse → 303 + 369 (bind) + 299 (enchant ride) + 283 (rideKey
  12010) ; le démontage natif = **NCID 85 sur l'enchant ride**
  (clic droit icône — l'ItemUse répété est refusé par le cooldown
  groupe 506, conforme au natif `CheckIsCoolDowning`) → 300 remove +
  283 rideKey=0 + 545 + refresh 296/492/493. `bind=1` persisté.

## 4. Montures de combat — anims d'attaque — ✅ FAIT (à retester)

- **Preuve** : `CEC_Ride::Execute` charge `MotionMgr::GetKfm("r%06d")`
  en char+0xd60 ; `CCharKfm::GetAttackID` @0x80e0b90 pioche DANS le
  KFM monture : `0x3fe - rand&1` (1021/1022), arc kind 0x1c → 1023,
  gun 0x1d → 1024. Un kfId d'arme n'existe pas dans r253 → pas d'anim.
- **Fait** : `NextPlayerAttackKf` monture-aware (gate RideType≠0,
  comme la conservation en combat). Commit `690f490`.
- Casts : `GetBlastID` natif n'a pas de branche ride → même blastId,
  le client résout les motions magic/shoot du KFM monture. Pas de
  changement serveur requis (à confirmer en jeu).

## 5. Cast en mouvement (client Reforged) — ✅ patché (à retester)

- Patch 1 fait : 2×`jbe`→`jmp` (0x75C4B0/0x75C862) — cast while moving.
- Steering RMB : `FUN_0075a8f0` @ 0x75aa0c teste `ctrl+0x3c` bit15
  (casting) et saute la copie heading caméra → `jne` remplacé par
  `90 90` (fichier 0x359E0C).
- Micro-stop : `FUN_0075ad10` (reset action→idle) appelé dans
  `FUN_0075c3a0` @ 0x75c615 et `FUN_0075c740` @ 0x75c9e0 → calls
  NOPés (5×90). La condition d'entrée `+0xa0 & 0x80000` (castable en
  mouvement) est conservée.
- Backup `game_.exe.bak_castmove`, détails dans
  `docs/PATCH-CLIENT-BINAIRE.md`. Commit `ccff885`.

## 6. Bind sur montures + unbind — ✅ FAIT + validé headless

- `BindItem` natif @0x08116870 s'exécute dès que le contrôle d'usage a
  tourné — y compris items non consommés (montures). Bind posé à
  l'usage monture + record 369 (tooltip « already binded »).
- `CEC_ItemUnBind` (cmd 1914) implémenté : UseType 3/5 = item-cible,
  Field6/7/8 = sel/clé/slot cible. Roll succès rand%10000<var0 (sens
  prouvé : `SuccessRate <= roll%10000` → branche ÉCHEC 0x23c7,
  Execute @0x853f820), var1% purge train attrs, var2% réduction
  renforcement (modes 0-2 = pondéré/moitié/zéro), var4 = niveau max
  cible, var5 = filtre UseType. Succès → bind=0 + 436 + 369 ; échec →
  0x23c7. Parchemin consommé dans les deux cas.
- ⚠️ vrais scrolls = items **mall 45150-45153/45250+/45258**
  (UseType=5, enchants 60355-60358 dans S_ItemMallEnchant). Les items
  32262/32265/32266 de S_Item ont « 1914 » en col36 — ce n'est PAS
  l'enchant d'usage (col36→CItemData+0x8c = licence/usage secondaire,
  UseType col12 vide → 0 → inutilisables).
- 🔴 Bug trouvé+fixé (`c9b4bb1`) : le dispatch UseType 3/5 du NCID 19
  routait vers `HandleItemOnItemAsync` (inlay kind45 / witchcraft
  56-79, kind≠45 → ack neutre) ; la branche unbind était dead code.
  Désormais `default` natif → `InvokeItemEffect` → cmd 1914.
- **Validé headless end-to-end** (ticket réel :2067 → zone :5201) :
  échec → 316 texte 0x23c7 + 303 + scroll consommé ; succès → 436 +
  369 + `bind=0` en DB (`/tmp/eden_unbind_probe.py`).
- Commits `2ea1de6` + `c9b4bb1`.

## 7. Doc méthode patch client — ✅ FAIT

- `docs/PATCH-CLIENT-BINAIRE.md` : méthode complète (offsets RVA↔fichier,
  backups `.bak_*`, vérification objdump post-patch, limites). Commits
  `451a4b9` + `ccff885`.

## 8. Guilde / famille — ✅ BUG BLOQUANT TROUVÉ + FIXÉ

- Audit NCID : les 262 CNE_CZ (client→zone) sont TOUTES routées
  (flow `case` ou dispatcher). Famille : 130/131/174/175/176/178/179/
  180/225/239 côté zone + CNE_CW ServerFamilyOp (22 sous-ops)
  /ServerQueryFamilyInfo/Emblem côté monde — couverture complète.
- 🔴 **Cause probable du freeze/crash UI guilde** : `player_characters
  .create_time` est un `timestamp` PG mais `CharRow.create_time` était
  `int` → `InvalidCastException` dans `FamilyRepository.LoadAllAsync`
  → **le WorldServer crashait au boot dès qu'une famille existait**
  (la table était vide jusqu'ici, bug dormant). Commit `11c9138` :
  `CAST(EXTRACT(EPOCH..) AS integer)` aux 2 SELECTs membres.
- **Validé headless end-to-end monde** (`/tmp/eden_world_probe.py`) :
  ticket T_ → world:5200 → ReceiveTicket → liste persos 38 →
  LoginCharacter 3 → séquence native 40/72/37 → QueryFamilyInfo 10 →
  50 fiche (type 0) + 50 membres (type 8) + 51 agenda + 68 logs +
  60 prestige — séquence conforme au handler natif.
- Reste : vérification visuelle du panneau guilde sur client réel.

## 9. Divers validés cette session

- Commandes GM AP/EP bout-en-bout headless : `//gain_ap_point 100` →
  pvalues 38→138, `//gain_ep_point 50` → bonus 0→50 (fnmember.tb_user).
  `//find_item` → 8 sabretooths (items normaux + mall fusionnés).
- 🔴 2ᵉ bug trouvé : race `ClientSessions.TryRemove(cid)` — une session
  obsolète dont le FIN arrive tard évincait la registration fraîche
  (reconnexion rapide → famille/amis/tell cassés en silence). Fix
  `5756967` : suppression conditionnelle par KeyValuePair.
- **Validé 2 sessions headless** (`/tmp/eden_guild_probe.py`) :
  invite op 0x01 → 52 leader + 48 invité (« test3/TestGuild ») ;
  accept op 0x03 (Field2=inviterId) → 46 reçu par les DEUX + 50 fiche
  + 50 membres. Join persisté en DB.
