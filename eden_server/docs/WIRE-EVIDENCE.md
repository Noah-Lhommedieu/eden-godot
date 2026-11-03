# Wire protocol — preuves désassemblage / captures

Référence unique. Chaque fait cite sa source : capture
(`captures/zsniff_native_entry.bin`, run test1 17:22-17:23 et
`captures/zsniff_cs_entry_success.bin`, run test3 18:03-18:28),
désassemblage ZoneServer (`server_us/ZoneServer/ZoneServer`, symboles
présents), ou données `S_/C_/T_*.ini`.

## NCID 310 `ClientEnterNodePlayerData` — layout PROUVÉ

Diff byte-par-byte des deux 310 natifs (test1 435B vs test3 417B — même
nom 5B donc offsets alignés jusqu'aux collections) :

```
@0   u32  charUid (LE du charId DB : test1=02c2eb0b=200000002)
@4   str  nom (u16 len + bytes GBK)
@11  str  (vide — 0000)
@13  f32  9.425 (cdcc1641 — taille/échelle ?)
@17  u8   0x0f
@18  u8   0x0f
@19  bin4 00 00 01 01
@23  u16  classId          — PROUVÉ : test1=03 (hunter), test3=01 (warrior)
@25  i64  ???              — test1=426, test3=376 (exp ? à confirmer)
@33  i64  hp               — 123 les deux (= hp/maxHp DB)
@41  i32  ???              — test1=175, test3=153
@45  i32  mp               — 69 les deux
@49  u8   01
@50  u8   00
@51  bin32 apparence       — test3 natif = 00*28 + `10 00 04 00` ;
                             test1 natif = `...10 00 34 68` (style diffère)
@83  u16  ???              — test1=2, test3=1
@85  i32  ???              — test1=14, test3=0
@89  i32  ???              — test1=60, test3=0
@97  i64  ???              — test1=294, test3=0
@105 i32  ???              — test1=90, test3=0
@109+ collections (mismatches à partir de @206/@335/@351 — contenu par
      perso : titres, succès…). Le 310 natif test1 fait 435B (18B de plus
      que test3) — contenu variable légitime.
```

**Interdit** : patcher @21/@22 (bin4), @34..@53 en i32 — ça détruit les
champs réels et produit le rendu "ursun"/"féminin"/"sans classe" et le
risque de désync. Patchs autorisés : nom (rebuild str+delta), classId
u16@23, hp i64@33, mp i32@45, apparence bin32@51.

## NCID 302 `ClientFlagModify` — sémantique PROUVÉE (capture + désasm)

Corps 9B : `[u32 handle][u16 flags][u8 sel][u8 op][u8 actionId]` —
en fait `[u32 handle][u32 mot-d'état][u8 seq]` : Serialize @0x81e9940
écrit `PutBinary(+8, 4)` + u8, et le ctor `CStateController::
NodifyFlagChange` @0x84e3c00 copie **le bitfield complet**
`entity+0x3a` (pas un delta) + le compteur `entity+0x280`. Il émet
DEUX fois : `BroadCastToCanSee` (observateurs) puis `NotifyClient`
(soi). `sel`/`op` = octets 2/3 du mot : sel=4 → bit18 « bataille »,
sel=0x14 → bit20 « phase attaque mob », op=1 → bit24 « base joueur ».

⚠️ **`sel=1` (bit16) n'existe dans AUCUNE capture** — c'est un état
FSM fantôme que le client ne purge jamais → personnage+caméra gelés
(jeu qui tourne) jusqu'à déco. Constaté live 17:58:11 : stop-attaque
`NCID=5 type=2` → réponse `sel=1` → plus aucun `NCID=1` ensuite.
Corrigé : le stop renvoie le mot courant (sel=4 si encore en combat,
sel=0 sinon) ; la migration envoyait aussi `sel=1` → corrigé à
`sel=0 op=1` (base 0x01000000).

- `sel` = champ d'état du controller ; `val` = compteur d'action par
  entité, croît à chaque NOUVELLE action (capture : 0x0a→0x11→0x12→…→0x24).
- flag[0] bit 0x2000 = "action offensive en cours" : posé UNE FOIS par
  engagement (`0020 sel=0 op=1 val=N` à la première action — dans la
  capture c'est un cast, jamais sur NCID=5 mêlée pure) ; purgé à la
  sortie (`0000 sel=0 op=1 val=N+1`).
- flag[4] bit 0x2000 = état combat soutenu : `0020 sel=4` puis
  `0000 sel=4` par cycle de cast.
- flag[4] op=0 (mob) = aggro ; flag[0x14] op=0 (mob) = phases d'attaque
  (val croissant 3,4,5,6).
- **La mêlée pure (NCID=5) n'émet AUCUN 302 pour le char** — le swing
  est local au client. Poser `0020 sel=0` à chaque engage mêlée laisse
  le flag "action" coincé → le client ne rejoue plus le swing. BUG
  CORRIGÉ à partir de cette preuve.

## Combat — séquences natives exactes

Requête mêlée `D NCID=5` : `[u32 mobHandle][u8 type]` ; `type=2` ou
handle=0 = stop → réponse `302 char 0000 sel=0` + `444 [0][0][0]`.

Coup mêlée (premier contact) : `272` → `267 [mob][f32x][f32z][u8 0x6a]`
→ `326 [mob][u16 08 01][f32][f32]` → `444 [mob][0][0]`.
Coups suivants : `272` seul. PAIRE de 272 par coup : `[atk][tgt][i32 dmg]
[i32 type][i32 hpLeft]` puis ~300 ms plus tard le même avec hpLeft=-1
(0xFFFFFFFF — notification). Types : 3 premier/fort, 1 courant,
5 fatal, 1 mob→joueur.

Cast `D NCID=9` : `[u16 spell][u32 mob]` → `279 [u16 spell][u32 0]
[u32 mob][u8 01]` → `302 char 0020 sel=0 (nouveau chain) | sel=4
(existant)` → `288 [i32 serial][u32 mob][u16 spell][i32 power]
[f32 x][f32 z][i32 0][i32 hpAvant][i32 0][u8 0]`.
Résolution ~0.3-0.7 s après (CEventDelayDamage côté natif) :
`271 [mob][00 01][char]` → `302 mob 0000 sel=4 op=0 v=1` →
`302 char 0020 sel=4` → `280 mob attr0 (hp,hp)` + `280 mob attr13 (100)`
→ `272(-1)` → `444 [mob][0][char]` → `302 char 0000 sel=4` →
`328 [char][u16 1][i32 f5ffffff][i32 0][i32 0]` → `280 mob attr0/attr13`
→ `281 [u16 spell][u32 mob][u32 char][i32 dmg][i32 1]` →
`280 mob attr0x21` → `299 [mob][u16 0xcb35/6][i32][i32][i32][i32][u8]`.

Mort du mob : `280 attr0=0` + `280 attr13` → coup fatal (`272 type5 -1`
ou `281`) → `305 [mob][char][u8 0]` → `280 attr0x21` →
`300 [mob][u16 attr][u8 act][i64 0]` ×2 mêlée (0xcb35,0xcb36) / ×1 sort.

Mort joueur : attrs 0 → `305 [char][mob][0]` → `280 attr0x21` →
`531 [i32 reviveArea]` → sortie combat `302 0000 sel=0` + `444 0`.
Revive (NCID=20) : `306 [u32 char][4B][u8]` + attrs + `313` position.

## Services PNJ — S_Npc.ini col 8 `#fn#arg#`

Codes prouvés par nom/rôle (décodage cp950) :

| fn | rôle | service |
|----|------|---------|
| 1,2,3 | 商人 (vendeurs) | shop 304, arg = S_Store id (1=armes, 2=armures, 3=items…) |
| 8  | 傳送陣 | transport 393, arg = groupe (50) |
| 6  | 金閃閃銀行 (Gold Sparkle Bank, NPC 50346/50347) | bank → **357** `[i32 npcUid]` |
| 18 | 水晶駱駝拍賣行 助理 (Crystal Camel Auction, NPC 50344/50345) | auction → **430** `[i32 npcUid]` |
| 23 | 聲望寶箱 | shop prestige (régional) |
| 55 | 次元精靈 | event |

Flux natif (désasm mission/decompiled.c) : `#fn#arg#` →
`ConvertNPCGreeting` crée greeting "-1" + `InsertNPCInitServiceDB` →
scénario `NPC[x] Init Service` dont la transition exécute
`Service <fn> <arg1> <arg2>` → `TC_Service` traduit en
`shop <verb> <sel> <ref> <extra>` → `TC_Shop` zone ouvre l'UI.
Table type→verbe désasm `TC_Service` @0x80ef980 :
1/2=`eq`, 3/4=`item`, 5=`create_family`, 6=`bank`, 7=`mail`,
8=`transport`, 9=`strengthen`, 10=`combine`, 11=`family_building`,
12=`mining`, 13=`planting`, 14=`summon`, 15=`alchemy`, 16=`casting`,
17=`enchanting`, 18=`auction`, 19=`family_storage`, 20=`message_board`,
21=`town_create`, 23/26/29=`item`, 24=`prescription_item`,
25=`family_town`, 27=`create_gladiator_team`, 31=`magic_box`,
35=`instance_dialog`, 50=`room_storage`, 51=`hotel_playertown`,
52=`hotel_holytown`, 53=`hotel_goldencity`, 54=`hotel_party`,
55=`sky_tower`, 56=`mission_shop`, 57=`church_reserve`,
58=`pay_system`, 59=`evaluate`, 60=`battle_crusade`,
10000=`mission-open`, 10001=`mission-reward`, 10002=`mission-on-going`,
10003=`mission-finish`.

`#:Service <title> <type> <ref>` (col 9+) → `InsertNPCServiceDB` crée
des dialogues scénarisés : le titre = T_Dialogue du client (102389 =
"Please tell me the detailed redemption list" pour les échanges).

**ctx du 56 (ListDialogue)** : `ctx ∈ {-1,0}` + npcUid≠0 → la zone
fait `ResetOperation` et N'ENVOIT PAS de 285
(`CNE_ZMis_ZoneServerListDialogue::Execute` @0x831fa50).

## Mail — flux PROUVÉ

- `95` open → `399 [i32 0]` (MailOpenBox::Execute — i32 seul, désasm
  0x826576e).
- `96`-`102` → zone→mission NCID 21 `AgentDBACommand`
  `[i32 charUid][str cmd][str "command"]` ; la mission répond
  68-77 → client 400-409 :
  - 68 → 400 `ClientMailList` : `[u16 count][count × row]` — vide = `00 00`.
  - 72 → 404 `[s16][s16]` = body[6..10] (4B EXACT — un 404 trop long
    désynchronise le flux → crash).
  - 69 → 401 `body[6..]` ; 70 → 402 `body[6..]` ; 71 → 403 `body[6..8]` ;
    75 → 405 `body[6..10]` ; 76 → 406 `body[6..10]` ; 77 → 409 `body[6..8]`.
- Ordre natif : 400 AVANT 404 — le lien mission doit sérialiser
  (chaîne, pas de fire-and-forget).

## Apparence (charlist + 310)

Blob 32B : test3 natif = `00*28 + 10 00 04 00`. Le tail `10 00 04 00`
varie par perso (test1 `34 68`). Les blobs à zéro dans la charlist
native rendaient les humains correctement — les modèles viennent des
champs race/genre/classe, le blob ajuste style/couleurs.
`10 00 64 59 01 00` = artefact de lecture à offset faux — produit un
rendu ursun. NE PAS utiliser.

## Divers wire confirmés

- `ClientDie` 305 = `[u32 victime][u32 tueur][s8]` (9B).
- `ClientRevive` 306 = `[u32][i32][u8]` (9B) — désasm Serialize.
- `ClientReviveInfo` 531 = `[i32]` (4B).
- `ClientPlayerAction` 290 = `[u32][u8][f32][u8][u8][i32][u8]` (16B) —
  broadcast aux AUTRES viewers, pas d'écho à l'émetteur (succès =
  silence ; échec = 410 `ClientCommandFailReturn [i32 ncid][i32 code]`).
- NCID 14 PlayerAction client = `[u8 action][i32][i32][i32]` — action 4
  = assis (capture live), 8 = état de spawn.
- `ClientEnterShop` 304 = `[i32][s16 storeId][u8][s16]` (désasm).
- `ClientEnterTransport` 393 = `[u16 n][n×s16 routes][s16][i32]`
  (désasm) — n = transports débloqués du perso.
- Transport `D NCID=88` = `[u16 routeId]` = clé S_Transport = aire dest.
- `ClientMeleeAttack` 272 = `[u32][u32][i32 dmg][i32 type][i32 hpLeft]`
  (20B corps).
- `ClientUpdateAttr` 280 = `[u32][s16 attr][i32 cur][i32 max][u32 0]`
  (18B corps) — attr 0 = HP broadcast, 0x11 = HP self, 0x12 = MP,
  0x13 = combat (100), 0x21 = post-hit.
- `ClientSeeMob` 298 = corps 76B (handle, eid, pos, hp, scale…).
- Framing : `[u16 len][payload chiffré]` par paquet — la longueur reste
  claire. Un octet en trop dans un body ne décale PAS les paquets
  suivants, mais une valeur corrompue (count, string, id d'asset)
  produit des lectures hors-liste → `CEGUI::UnknownObjectException`
  avec noms garbage (`'s'`, `'\x03@'`…) puis crash.

## Mail — séquence prouvée par désasm (session 17/10)

- Client 95 `ZoneServerMailOpenBox` = `[s16 op]` (+i32 toléré) — désasm
  `CNC_CZ_ClientMailOpenBox::Deserialize` @0x81e6270.
- `ZoneServerMailOpenBox::Execute` @0x8265510 dispatche sur op :
  - op **7** → ResetOperation (interne, aucun paquet) + NotifyClient
    → client **399** `ClientMailOpenBox` = `[i32 0]` (vtbl 0x87fb94c =
    `_ZTV..ClientMailOpenBox`, Serialize @0x81e3600).
  - ops 22/150/151/152 → autres sous-ops (ComposeOutput interne),
    pas de 399.
- Client 96 `MailQuery` = `[s16 page][s16 acceptOnlyFriend]` (4B) →
  forward mission `MailQuery page_number = %hd accept_only_friend_mail
  = %hd` via AgentDBACommand (NCID 21 lien mission).
- Mission→zone 72 `ZoneServerMailReceive` (12B : uid + 8B) → client
  **404** `ClientMailReceive` = `[u16 @payload+6][u16 @payload+8]` =
  `body[6..10]` (désasm Execute @0x8321600 : lit u16 @0x1a→+0x4 et
  u16 @0x1c→+0x6 de la commande client, corps 4B).
- Mission 68 `ZoneServerMailList` (~97B SoA de vecteurs, tous vides
  pour boîte vide) → client **400** `ClientMailList` = `[u16 count]
  [count×row]` — vide = `00 00`. Ordre natif : 400 puis 404.
- Apparence : blob 32B, marqueur `0x10` à [28] ; tails natifs prouvés
  `10 00 04 00` (test3) et `10 00 34 68` (test1). Tail `10 00 64 59` =
  CORROMPU (rendu ursun + Imagesets garbage → crashs fenêtres UI).
