# Constantes protocole Eden — lexique unique

> Régénéré depuis les commentaires de code + désasm (`re/ghidra/`).
> Chaque entrée cite sa preuve. **Ne pas dupliquer ces tables dans les
> commentaires** — référencer ce fichier.

## Attributs 280 `ClientUpdateAttr` / 328 `ClientModifyAttr`

Corps : `[u32 handle][s16 attrId][i32 p1][i64 p2]`
(Serialize 280 @zone, corps 18B — golden).

| attrId | Rôle | Preuve |
|---|---|---|
| `0x02` | Gold du personnage | `GainGold` → ClientUpdateAttr attr 2 |
| `0x06` | Exp courante | overlay d'entrée |
| `0x11` / `0x12` | HP / MP courant | burst d'entrée |
| `0x2c` | Classe active | overlay d'entrée |
| `0x30` | AP/points item-mall | `eden-reward` (attr 0x02 = or !) |
| `0x3f0` | « Inventaire prêt » (=1) — débloque l'affichage du sac | réponse au NCID 25 native |

## Attributs elf — 418 `ClientElfUpdateAttr` `[s16 loc][s16 type][i32 val][str ""]`

| type | Rôle |
|---|---|
| 1 | exp |
| 2 | hp |
| 8–11 | stats (grow) |
| 12 | name |

## États elf (`CKFElf.status`, persisté `elf_slots.status`)

| status | Signification |
|---|---|
| 0 | inactif (dans l'œuf) |
| 1 | ? |
| 2 | invoqué (pet live, `ElfPetHandle` posé) |

Un elf resté à 2 au crash redevient inactif au chargement.

## Types de containers item (274/312/358, `NativeItemCodec`)

| container | Rôle |
|---|---|
| 0 | inventaire (sac) |
| 1 | équipement |

## Types de dialogues services (`T_Dialogue`, NCID 56 → verbe natif)

| type | Verbe | Réponse |
|---|---|---|
| 1–4, 23/24/26/29 | eq/item | 304 `ClientEnterShop` |
| 5 | create_family | 347 |
| 6 | bank | 357 `ClientEnterStorage` |
| 7 | mail | 399 `ClientMailOpenBox` |
| 8 | transport | 393 `ClientEnterTransport` `[u16 n][n×s16][i32]` |
| 11 | family_building | 483 |
| 12–17 | métiers | 496 `ClientEnterWorks` `[i32][s16 workType][s16][s16][u16]` |
| 18 | auction | 430 `ClientAuctionHouse` |
| 19 | family_storage | 507 |
| 20 | message_board | 455 |
| 21 | town_create | 501 `[i32][s16 refId]` |
| 25 | family_town | 514 |
| 27 | create_gladiator_team | 524 `[i32][s16]` |
| 31 | magic_box | 612 (corps vide) + op 0x58 |
| 35 | instance_dialog | 564 `[s16 refId][s16 extra]` |
| 50–54 | room_storage/hotels | 539 `ClientOpenHotel` `[s16 type][i32]` (52→1, 53→2, 51→3, 54→4) |
| 55 | sky_tower | 579 `[s16 0xe][s16 0]` |
| 56 | mission_shop | 585 `[i32][s16 shopId]` |
| 57 | church_reserve | branche sans paquet |
| 58 | pay_system | 602 `ClientOpenPaySystem` `[i32][u16 shop]` (@0x81e6810) |
| 59 | evaluate | 579 `[s16 0x12][s16 refId]` |
| 60 | battle_crusade | ClientOpenBattleCrusadeDialog |

## Sight-handles (jamais des charId internes)

| Famille | Usage |
|---|---|
| `0x88C8xxxx` / `0x88C9xxxx` | NPC (replay : ≥ 0x88c8cec1) |
| `0xFFFFAxxx` | mobs (`0xFFFFAB0A` capturé) |
| charId | joueurs (le charId est lui-même le handle) |

## Combat / mouvement

| Constante | Rôle | Preuve |
|---|---|---|
| `0x4724` / `0x4725` | vitesse hf — bit0 = « a une destination » | capture 13B idle / 17B moving (326) |
| flag `0x07` | champ u8 du 326 pour un mob | captures |
| `0x6a` | action du 267 `ClientGoto` à l'engage | capture |
| 302 `[u32][u16 flags][u8 sel][u8 op][u8 act]` | NodifyFlagChange — émis uniquement quand un FSM d'état est retiré | désasm + golden 9B |
| 298 tail | masque u16 + champs optionnels en cascade (pas fixe !) | Serialize @0x081e96xx + 8 trames |
| enchant source pet | `pet+0x3f0` | `CNode::BornPet` natif |
| enchant 3001 / 3002 | HoT / DoT par seconde | `CEnchantMgr::OnTime` |
| char+0x280 | serial d'action (u8 tronqué) — champ des 271/299/302 | ctor natifs |

## Works / métiers (NCID 193 `Work*`)

| type service | workType |
|---|---|
| 12 | mining |
| 13 | planting |
| 14 | summon |
| 15 | alchemy |
| 16 | casting |
| 17 | enchanting |

`ToWork` exige `op+4 == type` ; `op` unique char+0x670 — une nouvelle
op détruit la précédente.

## Loterie / banque de trames

| NCID | Verbe entrant | Réponse |
|---|---|---|
| 25 | open_inventory | 312 + 280/`0x3f0=1` |
| 63 | LeaveShop | reset ShopOp* |
| 74 | vendeur | 375/376/377/378 |
| 56 | dialogue service | table ci-dessus |
| 193 | works | 345/352/500/572/599 |
| 18 | EnchantUpdate | 301 variable |
| 110 | — | 368 cooldowns `[u8 n][n×(u8,s16,u16,i32)]` |

## Encodages

- Chaînes wire = encodage natif enregistré via `EdenEncoding`
  (module init Eden.Core — CP950/Big5) ; `u16 len` + octets.
- Tout le protocole est little-endian (`InBinStream`/`OutBinStream`).
- Trame : `[u16 len][u16 ncid][corps]` — len = corps seul, hors
  l'en-tête de 4 octets (cf. `PacketReceiveBuffer`).
