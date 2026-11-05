# ref-shots/ — descriptions visuelles vérifiées (lecture directe)

Chaque entrée = image lue visuellement (pas seulement le catalogue).

## FusionPanel Craft (et guild npc crafter magic pool, forger...).png

Fenêtre **« Magic Pool »** recadrée sur le panel :
- En-tête : « …vailable for Class **LV 11** (and below) commission » + bouton « ? »
- Rangée haute : libellé **Use Item** + slot icône parchemin ; **Time** « 0 min. 10 sec. » ; **Cost** « 1080 🪙 0 🪙 » (or+argent) ; **Charm** + slot vide à droite
- « **Materials Required:** » à gauche, « **Success Rate: 1…%** » à droite
- Liste de 4 matériaux (icône + nom + compteur possédé/requis) :
  - Blueprint: Sanguine Fortunate → `1/1`
  - Sanguine Fragment → `71/40`
  - Infinity Core → `400/50`
  - Demonic Catalyst → `0/3` (grisé = manquant)
- « **…oduct:** » (Product) + slot produit : **Sanguine Fortunate Safety Stone** ×1
- Boutons bas : **Request** | **Close**

→ Correspond à `combinewnd`/`collectwnd` (UseItem+Charm+mats+coût+produit) — c'est la **spec visuelle de la commission de craft bâtiment**. Le coût 1080 or/10 s recoupe le K4 §1 (Sanguine Safety Stone, commission LV11).

## FindGuild.png

In-game (Limestone Mtn.), fenêtre **« Guild Recruitment »** :
- Table MCL : **Guild Name | Leader | Mbrs. | Lv | Guild Town** (lignes vides)
- Bas : bouton **Find Town**, pagination `<< < 1/1 > >>`, **Apply to Join**, **Close**
- Fenêtre ancrée gauche sous la fiche perso, « ? » d'aide en haut-droite

→ Spec de `familyfindingwind`/recherche de guilde : colonnes exactes + pagination + Apply to Join.

## Blueprint.png

Tooltip item **« Blueprint: Head Gem: Saint Blessing »** (qualité orange, icône parchemin bleu) :
- Section « Required Materials: » :
  - Blueprint: Head Gem: Saint Blessing *1
  - Etherworld Catalyzer *3
  - Demon Crystal *75
  - INT Magic Elimination Gem Lv5 *1
- Section « Finished Item: » : **Head Gem: Saint Blessing** (icône gemme bleue)
- Texte : « To inlay the item, right-click on it, and select a piece of equipment with sockets. » / « Can only be used on Lv75+ items. »
- Pied : `Gem | Head`

→ Spec du **tooltip de blueprint** : matériaux listés, produit incrusté, règles d'usage — pas une fenêtre. Confirme le format tooltip des plans (matériau×N + finisher + restriction Lv75+).

## item hover (plan mine ressources).png

Fenêtre **Shop** du « RedLight Inlay Merchant » : onglets **Items | Buy Back**, colonne d'items avec prix `160 🪙 228` (or+argent) pour « Lv85 Ore Location Map… » et « Lv90 Ore Location Map… » ; icône « ? » en haut-droite, scrollbar.

Tooltip sur « Lv90 Ore Location Map » :
- « Lv90 Ore Location Map **Work 11** » / « **Work Level 12** »
- « Rarity: Minerals that might be extracted: » **Crimson Diamond ×1, Bluefire Crystal ×1, Deepfathom Shell ×1, Lavender Crystal ×1**
- « "Minerals can be extracted from the town's mine" »
- Pied : `Mining Material` / `Can't be Traded` / `Level Requirement 90`

→ Spec **tooltip de plan de récolte mine** (work level, loot list, non-échangeable, lvl requis) + spec shop avec onglet **Buy Back** — le buyback apparaît dans l'UI boutique (file de 10, S3).

## Hover Item Plans ressources.png

Tooltip **« Formula: Leg Enchant - Gale Lv7 »** (orange, parchemin) :
- « Work Level 12 »
- « Required Materials: Enchant - Slightness Lv12 ×1, Red Spar Ore ×124, Flower Ink ×12 »
- « Finished Item: » **Leg Enchant - Gale Lv7**
- Rouge : « When casting an enchantment, hold down the right mouse button and select the equipment in your Backpack. Can only be used on Leg Gear that is Lv 88 and above. »
- Séparateur : `Enchanting Formula | Legs`
- **EVA +22**, **Move SPD +5%** (vert)
- `Level Requirement 88`
- Jaune : « "You can produce this item in the Magic Pools in towns." »
- `Magic Pool Material` / `Can't be Traded`

→ Spec tooltip **formule d'enchant de craft** : matériaux, produit, règle d'application (clic droit sur équipement), stats du produit, origine Magic Pool — confirme K4 §3 (Red Spar Ore ×124 = work level 8+).

## item hover (trophy enchant).png

Tooltip **« Trophy Enchant - Guardian Lv13 »** (icône violet-or) :
- Rouge : « When casting an enchantment, hold down the right mouse button and select a trophy in your Backpack. Can only be used on trophies that are Lv 90 and above. »
- `Enchanting Formula | Trophy Exclusive`
- **DEF +1365, HP +463, Block +93, Fire/Ice/Lightning/Nature/Holy/Dark Resistance +9, All Elemental Resistances +3, Malice +10 %** (verts)
- **ACC -8 %, G-Healing -83** (rouges/malus)

→ Spec tooltip **enchant de trophée** : bonus+malus colorés — confirme K4 §3 (Guardian Lv13, trophée Lv90+).

## Auction (hotel de vente) line item on sell time.png

Ligne de résultat de vente : icône item (stack **999**), nom coloré qualité (vert « Crimson Sequoia »), quantité **60**, prix `1500 🪙 / 996 🪙` (or+argent séparés, icônes).

## Auction (hotel de vente) multiple line item on sell time and colonne name.png

En-têtes de colonnes : **Style | Item Name/Quality | Lv | Selling Price** — lignes « Demon Ark » (icône sac violet, Lv 0) à `25000`, `24980`, `23000` (or+argent). La qualité colore le nom de l'item.

→ Spec de la MCL résultats HdV : colonnes exactes + qualité=couleur du nom + prix split or/argent (recoupe le catalogue).

## tradeNPCSystem.png — **fenêtre FR « Payer » (dons de gloire)** — spec majeure

Double panneau français, fenêtre « Payer » (fond mauve) :
- Gauche haut : « **Liste de paiement** » — liste des items de don de la faction : « Gloire des Amoureux de la Nature - Branche Sèche / Branche Dure / Pin Montagnard / Séquoia Nordique / **Séquoia Écarlate** (surligné) / Séquoia à Poison / Séquoia en Santé / Acajou Polaire / Séquoia »
- Gauche bas : « **Guide de contenu** » : « Union de mission?: Amoureux de la Nature » + description FR (« Nous observons les créatures vivantes… nous aimerions collecter des matériaux bruts en bois pour nos recherches, du ferait l'affaire »)
- Droite haut : « **Contenu du paiement** » : « Limite de paiement » (« Points de gloire Amoureux de la Nature Plus de 2001 pts… »), « Objet de paiement » : **Séquoia Nordique 0/15**
- Droite bas : « **Points de gloire** » + faction « Amoureux de la Nature » + score **195**
- Boutons bas : **Payer** | **Fermer**

→ **SEULE capture de panel en FR** : c'est `paysystemwnd` = la fenêtre de don d'objets de gloire aux 9 factions d'Aven (K5 §2). Confirme : liste des dons acceptés, compteur `possédé/requis`, palier de gloire affiché, description de faction.

## ItemMallPanel.png

Fenêtre **Item Mall** en jeu (Limestone Mtn.) :
- Onglets : **Item Mall | Loyalty Shop**
- Colonne gauche « Select a category » : **Limited Item / Featured / Crafting Items / Pets & Mounts / Gear / Boosts / Fashion / Colorful Dye**
- Centre « Catalog » : lignes d'items (icône+nom+prix en gemme+barre stock) avec badges **« 1 left this week! » / « 3 left this week! » / « 5 left this week! »** et boutons **Buy | Gift | BuyStac(k)** ; ex. Eternal Value Gift Box 49 ◆, Viridian Safety Stone… , Safety Stone…
- Colonne droite « **Hot Items** » : 30-slot pack, 20-Slot Pack 346 ◆, Eden Star Diamond 12 ◆, Prime Power Scroll 25 ◆, Prime Guard Scroll 18 ◆
- Bouton **Close** bas-droite
- **Tooltip « Viridian Safety Stone (NT) »** visible : « This brilliant gemstone is made of pure concentrated mana… Use this stone to protect an item during fortification and prevent it from breaking upon failure. **This stone can only be used to fortify +3~+6 gear. One Safety Stone is destroyed with each fortification attempt.** Note: Use of this stone will cause this item to bind to its user. » — pied `Item / Can't be Traded`

→ Spec complète `itemmall` (2 onglets + catégories + stock hebdo + Buy/Gift/BuyStack + Hot Items) **ET spec du tooltip pierre de sécurité** : plage « +3~+6 gear », consommé par tentative, lie l'objet — confirme K4 §1 pierres de sécurité.

## profile-fame onglet.png

Fenêtre **« InfosPersonnage » en FR** : onglets **Stats Personnage | Titre | Gloire | Succès** ; onglet Gloire affiché :
- « **Points de Gloire Généraux** » — liste factions avec icônes et scores : Amoureux de la Nature **36080**, Marteau d'Arthur **4025**, Association Arcanum **25120**, Pierre Sage **25190**, Chambre du Chameau **4145**, Aiguille d'Argent **6055**…
- Panneau bas « **Amoureux de la Nature** » : « Obtenir des Points de Gloire : Fournissez les Cultures requises à Tanja, l'Amoureux de la Nature à Aven. **Boutique de Gloire : Aven (344, 350)**. La guilde des Amoureux de la Nature cherche et cultive de nouvelles essences et est à l'origine de maintes découvertes. »
- En-tête fiche : nom « MonsieurX », LV75, classe Sage LV75

→ Spec FR de l'onglet **Gloire** : liste factions+scores + description par faction (PNJ de don + coordonnées boutique) — la fenêtre de consultation de gloire (K5 §2).

## item hover (trophée).png

Tooltip **« Holy - Neondo's Imperial Contract »** (or, trophée) :
- « "A contract proving Neondo has been appointed as a general of the empire. It reads 'Feel free to do anything beneficial for the Empire!'" »
- `Accessory | Trophy Exclusive` / `Can't be Traded / Already Bound`
- **Move SPD +5%** (vert), **Slash Resistance +12** (vert)
- Jaune : « When attacking, 5% chance to apply a debuff to the target for 10 seconds. »
- **Debuff: DEF -10%, All Resistances -10** (bleu/vert)
- `Level Requirement 90`

→ Spec tooltip **trophée de boss guilde** : header qualité+narration, tags accessoire/exclusif trophée, stats vertes, proc conditionnel en jaune + effet debuff — recoupe K2 §8 (trofées boss de guilde).

## ItemMallPanel-LoyaltyShop.png

Même fenêtre **Item Mall**, onglet **Loyalty Shop** actif :
- Catalog : **Prime Power Scroll 399 ◆** et **Prime Guard Scroll 299 ◆** (annotation `Buy Character Level: 90`), **Eternal Power Crystal 120 ◆** (lv30), **Life Leaf (NT) ~~490~~→290 ◆** (promo barrée, lv20), **Eternal EXP Talisman I 90 ◆** (lv30) / **II 290 ◆** (lv40) — chaque ligne porte `Buy Character Level: XX`
- Hot Items : Prime Power/Guard Scroll, Eternal Power Crystal 120, Eternal EXP/CP Talisman II 290, Pet Food (NT) 50, Bank Slot Rental (NT) 190
- Pied : **Collect All** + soldes `10721 ◆` (diamant/AP) et `0 ◆` (points loyauté), pagination 1/5

→ Spec de l'onglet Loyalty : prix en EP, **gate par niveau d'achat**, promos barrées, double solde — confirme K5 §8 (Crafting Items vendus en EP).

## item hover (basic ressources on classic mob on map).png

Tooltip **« Ancient Stone Elephant »** (blanc/gris) :
- « Seems to be quite an ancient item, someone might be willing to pay a high price to acquire it. »
- `Item` / `Can't be Traded`

→ Spec tooltip **matériau de base** (description + Can't be Traded) — le format le plus simple.

## Item hover consommable avec durability.png

Tooltip **« Light Beam Gun »** (rouge, craft zumi 75) :
- « Are you tired of annoying foes who are always trying to kill you at every turn? Carry this with you, and you can dispatch your enemies post-haste. »
- Jaune : « Note: Loses 1 Durability point per use. »
- `Item` / `Already Bound` / **Durability: 3/20**
- Vert : « In time, the body is enlarged to 200 % »
- `Cooldown: 1 min. 30 sec.`

→ Spec tooltip **consommable/gadget à durabilité** : la durabilité s'affiche `x/y` dans le tooltip — même mécanisme que les outils de récolte (pioche/gant/boule 180/540/1440).
