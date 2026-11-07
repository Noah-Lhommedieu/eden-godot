# Flux craft natif (sources autorisées uniquement)

- 496 client = `FUN_0070fe30 @0x0070fe30` (client_game) : `switch(workType)` 12/13/14 -> fenêtre **CollectWnd**, 15/16/17 -> **CombineWnd** (envoi d'événements 0x172 + 0x3e9, puis InventoryWnd à côté). Aucune worklist, aucun catalogue : 496 = juste l'ouverture (npcUid, workType, taxe, niveau bâtiment).
- 497 client = `FUN_0070ff50 @0x0070ff50` : ouvre **WorkListWnd** (0x3ec) + événement 0x174 sur WorkResultWnd. => worklistwnd = ordres EN COURS/terminés (tous types 12-17), pas un catalogue. 498 -> workresultwnd, 171 = récupération.
- CombineWnd = panneau « Magic Pool » du ref-shot « FusionPanel Craft (et guild npc crafter magic pool, forger...).png » : UseItemSlot (formule), ItemmallSlot (Charm), StuffItemSlot0-3 (matériaux possédé/requis), ResultItem, WorkLevelText, CommissionBtn/CloseBtn (combinewnd.layout). Même UI pour fonderie/alchimiste/mare magique (le-craft.md « Commander le craft »).
- Autorisation par PNJ : Kind (c_item col8) de la formule == 0x35/0x36/0x37 selon workType 15/16/17. Vérifié dans c_familycombine : cat 3 -> Kind 53/55, cat 4 (armes/armures, 1959 recettes) -> Kind 54, cat 5 -> 55. Serveur `WorkOrderService.ValidateStart` refuse déjà (BadCatalyst) : rien à changer.
- Recette = formule déposée : combine = 1er matériau de la ligne c_familycombine (col15) ; collect = col2 de c_familycollect. 170 envoie recette (collect : item id) + (bag,slot) de la formule.
- Taux PNJ (le-craft.md) : plan + plan x 10/20 /100. 
- Doutes : variante de PNJ (taux -10/-20 %) absente du 496 ; aides +25/50/75/100 % (guide) vs +30 serveur (déviation documentée) ; chaîne native des libellés (table 0x4e..0x58) non résolue, textes FR écrits à la main ; col1 cat 6/7 hors types 15-17 non étudiés.
