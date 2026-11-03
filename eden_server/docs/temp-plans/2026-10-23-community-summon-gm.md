# Plan live — 2026-10-23 : communauté, invocations, monnaies GM, find_item, skills

Suite de `2026-10-23-bag-capacity-crash.md`. Bugs remontés par le joueur
sur le client Reforged.

## Preuves déjà établies

### 52 ClientUpdateFriendMember — 13 champs, pas 12 (CRITIQUE)
`CNC_CW_ClientUpdateFriendMember::Serialize` (world/decompiled.c
@0x80cc120) écrit par membre :
`[i32 charId][str name][s16×6][str family][u8 online][u16 server][u8
state][u8 isPublic]`.
Le relais MW→CW (`CNE_MW_WorldServerUpdateFriendMember::Execute`
@0x812…) insère le champ `online` dérivé (`state != 0xff ? 1 : 0`) et
passe `state` brut (mission exec+0x604 : 0xffff = absent/offline).
Notre `SocialPackets.WriteFriendEntry` n'écrit que 12 champs — il
manque `u8 state` → tous les records après le 1er sont décalés →
panneau communauté totalement cassé.

### Modèle amis natif (mission TC_AddFriend @0x82386f0)
Table `friends` (player_id, friend_id, friend_type) :
0=blacklist, 1=moi-je-l'ai-ajouté, 2=eux-m'ont-ajouté, 3=mutuel.
AddFriend type=1 écrit IMMÉDIATEMENT les deux sens (offline ok :
`AddFriendData` persiste (cible→moi,2)) ; si l'entrée réciproque est
1 → les deux passent à 3. Pas de popup obligatoire — le 53
FriendInvitation n'est pas dans ce chemin natif.
`TC_InviteeFriend` : accept→3/3, sinon 1/2.
Blacklist : type 0 + paquet 61 (format `[u8 1][s16 n][n×{i32,str}]`).

### Recherche communauté
- Zone : `CNE_ZMis_ZoneServerSearchInfo` → 411 ClientSearchInfo —
  DÉJÀ implémenté (MiscHandlers:365).
- Monde : MutuallyHelpRegister(27)/Search(33)→81 — implémenté.
- NCID 10 ServerQueryFamilyInfo = panneau famille (u8 flag !=0) —
  natif ne répond RIEN si pas de famille (GetFamily==NULL → return).

### Invocations (CEC_SummonPet @0x855c280)
Commandes d'enchant : `SummonPet v0..v5` (2037/2052/2054/2073),
`SummonPetPlus` (2072), `DeSummon` (1071), `DistanceSummon` (1807),
`SummonInEventArea` (1074/1075/2045), `SummonEquipPet` (7001 elfe).
- var0 (this+8) = bonus de niveau, var1 (this+0xa) = premier id
  monstre, var2 (this+0xc) = dernier id — le natif parcourt la plage
  dans la map des templates et garde le monstre de PLUS HAUT niveau
  (monstre+0x10) ≤ caster_level + var0.
- var4 (this+0x10) = enchant d'apparition (défaut
  CMonster::DefaultSummonEffect = AllFormulaParas+0x114),
  var5 (this+0x14) = échelle d'effet, flag (this+0x16) → BornTotemPet
  (totem immobile) vs BornPet (pet mobile).
- pet+0x3f0 = l'enchant d'origine ; DeSummon/RemoveEnchantByID le tue.
- BornPet : position aléatoire ±N autour du caster, AI du template
  (pet suit et attaque la cible du maître), BroadcastToCanSee
  ClientPlayEffect.

### Monnaies
- AP/EP : `tb_user.pvalues`/`bonus` (fnmember) — world lit à la
  requête 25 PointInfo → 64 + achat 23 → 63. GM existant :
  `gain_aa_point` (AP), `gain_ep_point` (EP) → UserRepository.
- Autels : Eden Crystal 40241/40175(NT), Eden Gem 26283/26284,
  Star Diamond 27792 = ITEMS → `//give` suffit.
- special_coins.coinNN (types <12, 6-8 invalides) : GM natif
  `gain_special_gold <type> <money>` → zone relaie GMGainSpecialCoin
  à la mission → UPDATE special_coins → ClientUpdateSpecialCoin(78)
  `[u16 type][s16 idx][i32 newTotal]` au client.

### find_item
S_Item names = Big5 chinois ; les noms anglais sont dans
T_Item.ini (`id|Name|desc`) — jamais chargé. Charger → matcher
find_item/give sur le nom EN (exact d'abord, puis sous-chaîne).

### Skills de classe — persistance
player_spells : vérifier que les sorts de classe montés en niveau sont
sauvegardés avec leur LEVEL (pas seulement l'existence) et rechargés.

## Corrections

- [x] `WriteFriendEntry` : + `u8 state` (0xff offline / 0 online) —
      13 champs wire.
- [x] `FriendService` : modèle natif friend_type — `_relations` map
      (charId → friendId → type), chargée depuis la table `friends`
      via SocialRepository (migrer de player_friends → friends).
- [x] `SocialRepository` : API friends typées — GetRelationsAsync
      (friend_id+friend_type), SetFriendTypeAsync (upsert),
      RemoveFriendAsync, DeleteBothAsync.
- [x] `FriendHandlers.AddFriend` : type 1 → SetType(me→them)=1 +
      (them→me)=2, ou 3/3 si réciproque 1 ; type 0 → blacklist.
      Push 52 aux deux côtés connectés + entrées type 1/2 dans le 52.
- [x] `FriendInvitation` (opcode 14) : accept → 3/3 ; decline →
      supprime la ligne (them→me,2).
- [x] `RemoveFriend` : supprime les DEUX sens (natif RemoveMember
      bilatéral) + push 52 aux deux.
- [x] `GameDataService` : `ItemNames` (Dictionary<int,string>) depuis
      T_Item.ini ; `find_item`/`give` matchent nom EN exact puis
      sous-chaîne insensible.
- [ ] GM : `gain_special_gold <type> <n>` → UPDATE special_coins.coinNN
      + push 78 ; alias `gain_ap_point`/`gain_ep_point` déjà OK.
- [x] Skills : vérifier sauvegarde du LEVEL des sorts de classe
      (player_spells.level écrit à l'upgrade et lu au 311/entry).
- [x] Invocations : SummonService zone — map des monstres invoqués,
      sélection par plage var1..var2 + level cap, BornPet (follow +
      assist), BornTotemPet (fixe), DeSummon au re-cast/deco/logout,
      sight-handle + spawn/despawn broadcast + ClientPlayEffect.
- [x] Build Release 0 err/0 warn + tests + redéploiement.

## État final (2026-10-23, reprise post-crash)

- Build Release 0 err/0 warn, 402/402 tests verts.
- Stack redéployée (Login 6543, Ticket 2067, Mission 7654, World
  5200/5301, Zone 5201, EDEN_NET_DEBUG=1).
- Compléments livrés ce round :
  * `RewardSpecialCoin` : le script porte le TYPE (0-11) → colonne
    coin{type+1} (off-by-one corrigé + type 0 accepté).
  * `MissionDb.AddSpecialCoinAsync` → retourne le nouveau total
    (RETURNING) + clamp 0..9999999 natif.
  * `case "GMGainSpecialCoin"` mission : validation type 0-5/9-11,
    texte 0x1100/0x1101 + nom S/T_TextIndex (type+0x95f, 0x1a7f,
    0x1cfe, 0x1dad), reply zone NCID 80.
  * Relais zone (case 80) → ZW NCID 30 → monde pousse CW 78
    `[u16 n][n×(s16 type, i32 total)]` au client.
  * Push CW 78 au login monde (tous les coins non-nuls).
  * `ParseTextIndex` : T_TextIndex (2 col.) retournait des chaînes
    VIDES — corrigé (col3 vide → col2 ; lignes `id|texte` acceptées).
  * Tuple `GetPlayerSpellsAsync` renommé `Order`→`Level` (sémantique
    spell_level — le wire 311 écrit CSpellMaster+0x20 = level).

## Addendum post-crash — finalisation

- `gain_special_gold` vérifié de bout en bout : GmHandlers → zone
  (mission 80 UpdateSpecialCoin → MissionDb.AddSpecialCoinAsync
  coinNN upsert + clamp 0..9 999 999) → relais zone ZW 30 → monde
  push CW 78 `[u16 n][n×(s16 type,i32 total)]`. Login push présent
  (`Eden.World/Program.cs:195` — GetSpecialCoinsAsync → 78).
- Skills de classe : `spell_level` (pas `spell_order`) désormais
  émis partout — 286 (login + level-up, champ 4 = CP restant
  désasm SetSpellLevel local_28=char+0x488), vec sorts du 311
  `[u16 smId][u8 level]` (Serialize : `*psVar14`, `(char)psVar14[0x10]`).
  Tuple repo renommé Ord→Level ; UseSkillPoint audité (gates paytype
  1=or/9=CP, class_lv pour familles 6/7, coût S_SpellReference).
- 611 ClientChangeSpell (3 vecs parallèles smIds/?/levels, désasm
  SetSpellLevel @0x8156xxx) : vec2 non prouvé → NON émis (règle :
  aucun paquet spéculatif). Le 286 suffit pour la notif d'upgrade.
- Build Release 0 err / 0 warn, 402/402 tests verts.
