# Plan — audit items EDENFR exhaustif + refonte catégories archive

## Audit mesuré (corpus/official + server_us)

| Gap | Volume | Détail |
|---|---|---|
| items sans def serveur | 0 | couvert par S_ItemExtra (236) |
| combos sans def | 5 | ids 3629-3633 (c_itemcombo) |
| chests sans def | 90 | ids 10001-10090 — coffres cosmétiques 2M/Visage/Tête (%) EDENFR |
| enchants manquants | 67 | corpus client ET serveur — dont montures (scorpions 60139/60140, tigre 60175), orbes, pierres anti-break, 60337 renard glace légendaire |
| items sans nom localisé | 215 | à analyser |
| montures (2134) | 305 | à vérifier → modèles glb |

Combo/chest : chargés depuis DATA_DIR natif SANS overlay (Program.cs
S_ItemChest direct, GameDataService.Load S_ItemCombo) — d'où le trou.

## Lot 1 — combler les gaps

1. `corpus/official/data/db/S_ItemChestExtra.ini` + `S_ItemComboExtra.ini`
   (nouveaux overlays, même convention que S_ItemExtra) — copie verbatim
   des lignes EDENFR absentes ; merge dans Program.cs (ParseItemChests)
   et GameDataService (ItemCombos).
2. 67 enchants → `corpus/official/data/db/c_enchant.ini` (l'overlay est
   déjà lu par EnchantService côté serveur et par le client).
3. build_official.py : régénérer tout ça (durable).
4. Renard de glace : enchants 55867/55868/60337 inclus dans le lot
   (43801/43802 ont déjà defs+cartes).
5. Noms manquants : importer depuis EDENFR t_* quand ils existent là.
6. Modèles montures : vérifier glb pour les 305 items → liste des absents.

## Lot 2 — refonte catégories archive (UI)

Plainte : montures dans « Objet », armes non triées par type, armures
pareil, costumes à séparer par partie — via menus déroulants.

- Lire ArchiveWindow + format c_collection (cat col5, groupe col8).
- Ajouter sous-catégories déroulantes par famille :
  Montures (toutes), Armes par type (épée/hache/bâton/arc/dague/…),
  Armures par emplacement, Costumes par partie, Objets divers.
- Source du type d'item : c_item (kind/type cols) + c_itemmall.

## Vérifs

- dotnet build serveur + client ; dotnet test ciblé.
- `//clone_item` sur chest 10001 + un nouvel enchant monture.
- Archive live : compte par catégorie, dropdowns, flameshot.

## Bilan final (audit #2 — demande « TOUT EDENFR dans l'archive, jouable »)

### Renards — 7 stems tous fonctionnels en jeu
| Stem | Items | Preuve |
|---|---|---|
| r253 | 1290, 43774/5, 44413/4 | monté (kitsune doré) |
| r254 | 43801/2 (légendaire glace) | monté live (254010) — **anim regénérée** |
| r255 | 44113/4 (Flamombre) | visuel monté (255010) |
| r267 | 48799 | give+visuel (267010) — **def+anim réparés** |
| r268 | 48813 (blanc légendaire) | monté live (268010) |
| r269 | 48815 (or) | **give→use→ride 60421→269010 prouvé** |
| r270 | 48817 (pourpre) | give+visuel (270010) |

### Corpus / hygiène archive
- Defs serveur manquantes posées : 48799/48815/48817 (stubs I00132
  remplacés), masques 44047/44048 (clone 40759, enchant lié même id).
- Cartes fantômes 44385/44386 retirées (enchant E1023, jamais items).
- Fallback FR→EN dans `TextResolver.Ensure` : ~1466 cartes 3xxxx et
  ~700 4xxxx affichaient le Big5 brut en FR — repli sur `lang/en`.
- Reproductible : EXTRA_ROWS c_itemmall + _COLLECTION_DROP +
  EXTRA_FILES dans build_official.py.

### Assets — corruption trouvée et corrigée
- **217 sources corpus corrompues** (même taille, contenu divergent
  vs EDENFR/extracted — parse impossible) : 101 .kf ride + 116
  .nif/.kf char/monster/effect → remplacées par les originaux EDENFR
  et reconverties. GLB vides : 127 → 0 réel (22 restants = effets
  particules-only natifs, sans mesh par design).
- r267/r268 (renards) avaient 0 anim — maintenant 27 chacun.
- Textures <100o = placeholders 4×4 valides (CharDummy etc.).

### Serveur / DB
- MISSION_PORT=5202 pollué dans l'env shell cassait le lien ZM →
  explicité 7654 dans start-native.sh (lien établi, 67 nodes).
- Zone portage tree chargée : 29077 items, overlay enchant corpus
  (21233 remplacés + 200 ajoutés).
- DB : jointure guilds→family corrigée (family_name vide), migration
  001 ne détruit plus player_mail native, 17 tables maison mortes
  archivées dans schéma `archive` (016, réversible), compose initdb
  complète 002-016.

### Non vérifié
- `dotnet test` complet (builds verts seulement).
- Ouverture live d'un chest/combo extra (merge silencieux chargé).
- Particules/auras de monture : pas de bridge GPUParticles dédié —
  audit = les montures natives n'ont pas d'aura dédiée dans les data.
- Pack EPK non rebuild.
