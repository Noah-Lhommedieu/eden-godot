# Plan — reboot combat freeze + pets + chase + EXP chat (2026-10-21)

Reprise à zéro des incidents live rapportés après le lot précédent.
Règle : **aucun item n'est « fait » sans preuve byte/désasm ET validation
client**. Aucun paquet spéculatif.

## Symptômes live (priorité bloquante)

| # | Symptôme | Hypothèse principale | Preuve à collecter |
|---|---|---|---|
| F1 | Pets (kind 60) s'équipent sur un slot armure au lieu du panneau familiers | `EquipItem` n'intercepte pas kind 60 — seul `UseItem` le fait | `S_Item` 23993 EquipLoc/Kind ; natif `Equip` kind-60 |
| F2 | Chat « EXP Acquired » vide : perso/classe/gear/pet | On n'émet pas le paquet/chat feed d'XP | Désasm gain XP natif (packet après kill) |
| F3 | Ne peut plus bouger **pendant** ni **avant** le cast | Le 279/278 ou un 302 lock le client ; autrefois un paquet permettait le mouvement | Client `game_.exe` : ce qui autorise MoveSelf pendant cast ; natif `CastToLife` + MoveSelf pendant cast |
| F4 | Mobs ne suivent pas assez (1 pos / 1-2 s) | Tick chase 300 ms encore insuffisant — cible : ~10 updates/s | Réduire tick à ~100 ms + step proportionnel |
| F5 | Freeze total en engageant plusieurs mobs ; se débloque (ou pas) à la mort du dernier | **Désync stream** : un paquet combat à mauvaise longueur corrompt tout ; ou un 302/280/444 qui fige l'état client | Audit byte-par-byte de CHAQUE paquet combat émis vs capture/désasm |
| F6 | Le mob se **téléporte** sur le joueur à l'aggro | Premier paquet de chase envoie position/direction erronées (poignée, échelle ×16, ou flag) | Format natif 267 vs notre émission |
| F7 | Manquent commandes GM, montures, license knowledge/class talent (KPs), class ability bonus (CE) | À auditer — certains partiels | Matrice existante + désasm |
| F8 | Revoir TOUT le plan prompt3 | Matrice 36 lignes | Refaire ligne par ligne |

## F5 — Audit longueurs paquets combat (le plus probable)

Chaque paquet émis pendant un engagement doit avoir sa longueur
comparée au `Serialize`/capture natif. Une seule erreur = désync
totale → freeze client (caméra, mouvement, ciblage morts alors que
le serveur tourne encore — le « mob 0 HP qui tape » en est un
symptôme : le client a raté le 305).

Candidats : 271, 272, 278, 279, 280, 281, 287, 288, 290, 292, 302,
305, 326, 328, 410, 444, 495, 513, 264.

Méthode : pour chaque NCID émis dans `ZoneEntryFlow.Combat.cs` /
`.Movement.cs` / `.Elf.cs`, trouver le `Serialize` natif dans
`re/ghidra/zone/decompiled.c` (ou la capture) et compter les
octets. Logger `ncid len` en EDEN_NET_DEBUG pour comparer en live.

## F3 — Cast + mouvement

Le client lock probablement le mouvement entre le 279 (barre de cast)
et la résolution. Pistes :
- Le natif MoveSelf pendant cast est-il accepté ? (il l'est côté
  serveur — le client l'envoie déjà, donc c'est le client qui bloque).
- Chercher dans `rf_game`/`client_game` le handler 279/302 qui pose
  un flag « casting » interdisant le déplacement, et ce qui le lève
  (281 ? 288 ? break ?). S'il manque un paquet de fin de cast, le
  client reste figé — cohérent avec F5.

## Étapes

1. [ ] Audit longueurs paquets combat vs natif (tableau ncid/len
       émise/len native/statut).
2. [ ] F1 : intercepter kind 60 dans EquipItem → AddElf (+refus si
       pas de slot libre → l'item reste).
3. [ ] F6 : vérifier/corriger le premier paquet chase (267) — pas de
       téléportation.
4. [ ] F4 : tick chase ~100 ms, step cohérent avec la vitesse 326.
5. [ ] F3 : mouvement libre avant et pendant le cast (trouver le
       paquet/flag qui lève le lock client).
6. [ ] F2 : notifications XP chat (perso/classe/gear/pet) au kill.
7. [ ] F7 : audit GM commands / montures / KP / CE bonus — livrer ce
       qui manque le plus bloquant.
8. [ ] F8 : matrice prompt3 re-vérifiée ligne par ligne.
