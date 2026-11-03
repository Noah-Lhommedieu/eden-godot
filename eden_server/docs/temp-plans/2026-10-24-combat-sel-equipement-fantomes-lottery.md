# Lot — mot d'état 302, icônes fantômes équipement, lottery weekClaim, find_item

Date : 2026-10-24

## Symptômes / preuves live

- Trace 18:30:08 : cast sur un 2ᵉ mob pendant qu'un autre cible le
  joueur → 302 `0020 sel=0` = mot d'état `0x01002000` — le bit18
  « bataille » s'efface alors qu'un mob cible encore → le client
  affiche « leaving combat » (scintillement).
- Trace 18:35-18:36 : `NCID=129` GoLottery `altar=1 item=0x66AC
  req=0 p4=-1` sans AUCUNE réponse (pas de 448/433) — le client
  retente en boucle.
- Items jetés qui « réapparaissent » : prouvé = `clone_item 26284`
  (GM), pas le drop. Le DELETE DropItem fonctionne (log « ok » + DB).
- Équipement « dans les mauvais slots » : DB propre (armes loc7/10,
  armures loc2/4/5, rien de travers) — icônes fantômes client.
- `find_item eden gem` → 0 résultat : la recherche matche la chaîne
  ENTIÈRE — « Eden Amethyst » ne contient pas « eden gem ».

## Preuves natives

- NCID 302 = `CStateController::NodifyFlagChange` @0x84e3c00 : le
  corps transporte `entity+0x3a` = **mot d'état FSM complet u32** +
  compteur `+0x280`. `sel` = bits 16-23 → sel=4 = bit18 « bataille »,
  sel=1 = bit16 = état jamais purgé → freeze (corrigé avant).
- `CPlayerFactory::GoLottery` @0x811a5e0 (autels base/diamond) :
  `param_4 != 0` → `LotteryReceiveWeekCountItem` AVANT `param_3`.
- `CPlayerFactory::GoInGameLottery` @0x811b130 (autel 1) : ne lit
  JAMAIS `param_4` — `param_3 == 0` → `InGameLotteryReceiveItem`.
  Le client envoie p4=-1 même pour un claim normal.
- Routage équipement : le client résout le slot LUI-MÊME au drag
  (sa table EquipLoc, ex. item pet EquipLoc=2 → peint en slot body)
  et envoie `dstSlot=-1` → nos gates serveur (kinds) refusent après
  coup → l'icône reste posée. Le 311 n'est rechargé qu'à l'entrée
  de zone → un 275 (type=1, loc) est le seul paquet qui purge un
  slot en jeu.

## Modifications

1. **302 sel = bit18** : les 4 chemins de cast
   (`ZoneEntryFlow.Combat.Spells.cs` cast-start,
   `Spells.Resolution.cs` ×3) émettent désormais
   `st.InCombat ? SEL_COMBAT : 0` — plus de `chainStart ? 0 : SEL`.
   La release aggro (`Resolution.cs:243`) idem. `chainStart` ne
   sert plus qu'à allouer l'act.
2. **Purge fantôme 275** : `PurgeEquipGhostAsync` (Helpers) envoie
   `ClientDropItem [type=1][loc][grp]` uniquement si le loc est VIDE
   en DB — purge l'icône sans toucher les vrais items. Candidats =
   `GhostLocCandidates` (dstSlot explicite ou loc canonique client +
   paires dual 7↔8/12↔13/14↔15). Câblé sur tous les refus
   (0→1, 1→1, 1→0) + le resync 311 conservé.
3. **Lottery** : `weekClaim != 0` n'intercepte plus l'autel in-game
   (`WeekColumn<0`) — `req==0` → ReceiveItem comme le natif.
4. **find_item/FindItem** : tous les mots doivent matcher
   (name+code+T_Item anglais concaténés) — « eden amethyst »,
   « alpaca mount » trouvent désormais.

## Régression corrigée (crash équipement)

- Trace 19:33:41 : switch refusé (0,21)->(1,-1) → 311 len=1119
  mid-session → déconnexion immédiate = crash client. Le client ne
  traite le 311 que dans la séquence d'entrée de zone.
- Fix : `ResyncEquipmentPaneAsync` supprimé — plus AUCUN 311 hors
  `SendPatchedAsync` (entrée). Les refus n'émettent que 275 (purge
  ciblée, traitée en jeu) + 277.

## À vérifier côté client

- Cast sur 2ᵉ mob : pas de « leaving combat ».
- Déséquipement d'une icône fantôme → purge immédiate.
- Autel in-game (95 op 150) → Go/claim répond (448/433).
- `//find_item eden amethyst`, `//find_item alpaca` → résultats.
- Cast en mouvement : gate CLIENT natif (C_Spell col27 = cast ds +
  état déplacement, `FUN_0075c3a0` rf/client identique) — les sorts
  à cast time ne se lancent pas en marchant en retail. Un support
  custom exige une modif client (donnée ou binaire), pas serveur.
