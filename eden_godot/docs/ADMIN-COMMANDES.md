# Commandes admin dans le chat (test / debug)

## Utilisation

Dans la zone de saisie du chat (Entree pour la focaliser), taper la commande avec **`//`** ou **`/`** :

```
//clone_item 21345        (equivalent : /clone_item 21345)
//find_item mount         (cherche un id par nom EN ; le nom S_Item est en Big5)
//give 21345              (id) ou //give <nom EN> [qty]
//clear_bag 0             (vide le sac 0 : « Inventory full » sinon)
//tele 420 300            (colline degagee)
//help                    (liste complete, implementee ou non)
```

Chemin : `ChatPanel.OnSubmit` (un `/xxx` qui n'est pas un canal `/s /o /p /g /z /w /b` devient `//xxx`)
-> `GameScene.SendChatAsync` (`//` -> `ZoneTextCommand` NCID 3) -> `ZoneEntryFlow.HandleTextCommandAsync`
(switch local puis registre `GmCommandService` : `GmHandlers`). Reponse en texte rouge dans le chat.

## Pourquoi « ca ne marchait pas » (causes corrigees / a connaitre)

1. **Privilege du perso = 0** : le serveur ignore la commande (gate natif `privilege > 0`, reponse
   0x5d0 « commande inconnue »). Le privilege est une colonne `player_characters.privilege`, mise a 0 a la creation.
   Fix : `CreateCharacterNativeAsync` donne 5 si `fnaccount.accounts.role = 'Admin'` (comptes `admin`, `test123`).
   Perso existant : `UPDATE player_characters SET privilege=5 WHERE id=...` (base `fndb1`).
2. **`/commande` avec un seul slash** partait comme message de chat : corrige cote client.
3. **« [GM] don impossible : Inventory full »** : sac plein -> `//clear_bag 0` puis re-donner.
4. `//find_item` cherche dans le nom EN (T_Item) : « mount » marche, « monture » non.

## Donner les montures de test

```
//clear_bag 0
//clone_item 21345    (r151, dragon, +75 %)
//clone_item 21252    (r503, +75 %)
//clone_item 26495    (r109, +90 %)
//clone_item 20697    (r511, +40 %)
//clone_item 20331    (r013, +35 %)
```
Puis sac (icone en bas a droite), clic droit sur l'item -> « Utiliser / Equiper » = monter/descendre.

## Debug camera montures

`echo "-1 90 8 8 4.5" > /tmp/ride_key.txt` : `key yaw pitch dist hauteur` ; **key = -1** = camera seule
(la monture posee par le serveur reste) ; key > 0 force le modele (cf. `MONTURES-L17.md`).
