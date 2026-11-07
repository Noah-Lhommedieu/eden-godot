# Balayage donjons (faits observés en jeu)

Source : `tools/run.sh -- --live` + commandes GM (`transport_node`, `tele`, commandes de balayage `waitnode`/`probe`/`shot`/`quit` ajoutées à `--zone-cmd`), perso test en god mode. Une capture par donjon dans `artifacts/dungeon-sweep/SNNN.png`. Les 40 nodes de type 5 sans scène ne sont pas jouables (aucune scène dans corpus/fr-full).

Légende : éclairage = couleur moyenne du centre de l écran (classe grossière : sombre / bleu / ok, à juger sur la capture) ; sous sol = mobs avec un sol à ≤12 u au-dessus ; sans collider = mobs sans aucun sol physique (hauteur non vérifiable, z de scène estimé) ; boss = HP c_monster attendu → HP max vu au point du boss.

| node | map | scène | éclairage (moy RGB) | mobs vus (attendus) | sous sol / sans collider / dy max | immobiles/chemins (données) | boss (HP attendu → vu) | minimap |
|---|---|---|---|---|---|---|---|---|
| 35 | S035 | oui | ok (91, 94, 102) | 2 (0) ✓ | 0 / 2 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 39 | S039 | oui | ok (114, 77, 97) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 40 | S040 | oui | ok (161, 184, 183) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 41 | S041 | oui | ok (156, 159, 177) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 43 | S043 | oui | ok (95, 74, 100) | 3 (0) ✓ | 0 / 0 / 0.6 | 0 fixes / 0 en chemin | - | ok |
| 45 | S045 | oui | ok (107, 106, 62) | 11 (0) ✓ | 0 / 11 / 80.3 | 0 fixes / 0 en chemin | - | ok |
| 46 | S046 | oui | ok (126, 152, 147) | 4 (0) ✓ | 0 / 4 / 53.6 | 0 fixes / 0 en chemin | - | ok |
| 47 | S047 | oui | ok (171, 136, 117) | 5 (0) ✓ | 0 / 5 / 24.6 | 0 fixes / 0 en chemin | - | ok |
| 48 | S048 | oui | ok (60, 67, 83) | 2 (0) ✓ | 0 / 0 / 0.5 | 0 fixes / 0 en chemin | - | ok |
| 49 | S049 | oui | ok (137, 148, 179) | 9 (7) ✓ | 0 / 9 / 3.1 | 7 fixes / 0 en chemin | 60000 → 999999 ✓ | ok |
| 50 | S050 | oui | bleu (53, 69, 108) | 12 (0) ✓ | 0 / 12 / 180.2 | 0 fixes / 0 en chemin | - | ok |
| 100 | S100 | oui | ok (129, 122, 139) | 6 (6) ✓ | 0 / 6 / 3.1 | 6 fixes / 0 en chemin | 990 → 990 ✓ | ok |
| 101 | S101 | oui | ok (155, 124, 79) | 6 (41) ✓ | 0 / 6 / 3.1 | 37 fixes / 4 en chemin | 10269 → 10269 ✓ | ok |
| 102 | S102 | oui | ok (131, 124, 118) | 6 (63) ✓ | 0 / 0 / 0.9 | 49 fixes / 14 en chemin | 22743 → 22743 ✓ | ok |
| 103 | S103 | oui | ok (26, 171, 156) | 5 (25) ✓ | 1 / 4 / 3.1 | 23 fixes / 2 en chemin | 27144 → 27144 ✓ | ok |
| 104 | S104 | oui | ok (62, 59, 84) | 11 (33) ✓ | 0 / 11 / 3.1 | 29 fixes / 4 en chemin | 31332 → 31332 ✓ | ok |
| 105 | S105 | oui | ok (185, 138, 119) | 12 (37) ✓ | 0 / 12 / 3.1 | 29 fixes / 8 en chemin | 38282 → 38282 ✓ | ok |
| 106 | S106 | oui | ok (99, 86, 73) | 18 (62) ✓ | 0 / 18 / 0.7 | 55 fixes / 7 en chemin | 46067 → 46067 ✓ | ok |
| 107 | S107 | oui | ok (236, 235, 191) | 8 (35) ✓ | 0 / 8 / 3.1 | 30 fixes / 5 en chemin | 57807 → 57807 ✓ | ok |
| 108 | S108 | oui | bleu (9, 115, 162) | 22 (71) ✓ | 0 / 22 / 3.1 | 62 fixes / 9 en chemin | 128586 → 128586 ✓ | ok |
| 109 | S109 | oui | ok (165, 195, 188) | 21 (50) ✓ | 0 / 21 / 3.1 | 44 fixes / 6 en chemin | 71312 → 170200 ✓ | ok |
| 110 | S110 | oui | sombre (120, 2, 1) | 8 (23) ✓ | 0 / 7 / 3.1 | 17 fixes / 6 en chemin | 86409 → 999999 ✓ | ok |
| 111 | S111 | oui | ok (164, 127, 162) | 13 (64) ✓ | 0 / 12 / 3.1 | 58 fixes / 6 en chemin | 189467 → 999999 ✓ | ok |
| 112 | S112 | oui | ok (207, 199, 176) | 15 (68) ✓ | 0 / 14 / 3.1 | 65 fixes / 3 en chemin | 113932 → 999999 ✓ | ok |
| 113 | S113 | oui | bleu (76, 64, 146) | 12 (79) ✓ | 0 / 11 / 3.1 | 69 fixes / 10 en chemin | 122885 → 999999 ✓ | ok |
| 114 | S114 | oui | ok (139, 128, 103) | 16 (82) ✓ | 0 / 0 / 0.4 | 74 fixes / 8 en chemin | 256147 → 256147 ✓ | ok |
| 115 | S115 | oui | ok (181, 177, 194) | 7 (54) ✓ | 0 / 6 / 3.1 | 45 fixes / 9 en chemin | 133417 → 999999 ✓ | ok |
| 116 | S116 | oui | ok (136, 129, 83) | 32 (64) ✓ | 1 / 31 / 3.1 | 58 fixes / 6 en chemin | 173492 → 173492 ✓ | ok |
| 117 | S117 | oui | ok (162, 9, 8) | 11 (39) ✓ | 0 / 10 / 3.1 | 35 fixes / 4 en chemin | 187680 → 999999 ✓ | ok |
| 118 | S118 | oui | ok (195, 110, 109) | 7 (37) ✓ | 0 / 7 / 3.1 | 31 fixes / 6 en chemin | 195085 → 195085 ✓ | ok |
| 119 | S119 | oui | ok (132, 145, 164) | 29 (54) ✓ | 0 / 29 / 1.0 | 50 fixes / 4 en chemin | 210550 → 999999 ✓ | ok |
| 120 | S120 | oui | bleu (3, 151, 187) | 8 (47) ✓ | 1 / 7 / 3.1 | 38 fixes / 9 en chemin | 218623 → 218623 ✓ | ok |
| 121 | S121 | oui | ok (185, 179, 36) | 14 (59) ✓ | 0 / 14 / 3.1 | 52 fixes / 7 en chemin | 453864 → 453864 ✓ | ok |
| 122 | S122 | oui | ok (187, 171, 208) | 7 (84) ✓ | 0 / 7 / 3.1 | 79 fixes / 5 en chemin | 635326 → 635326 ✓ | ok |
| 123 | S123 | oui | ok (148, 153, 206) | 5 (65) ✓ | 0 / 5 / 3.1 | 63 fixes / 2 en chemin | 682164 → 682164 ✓ | ok |
| 124 | S124 | oui | ok (149, 83, 30) | 10 (54) ✓ | 1 / 9 / 3.1 | 49 fixes / 5 en chemin | 612915 → 612915 ✓ | ok |
| 125 | S125 | oui | ok (184, 233, 246) | 8 (72) ✓ | 1 / 7 / 3.1 | 66 fixes / 6 en chemin | 658401 → 658401 ✓ | ok |
| 126 | S126 | oui | ok (166, 135, 157) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 127 | S127 | oui | sombre (3, 20, 17) | 9 (59) ✓ | 0 / 9 / 3.1 | 55 fixes / 4 en chemin | 2133349 → 2133349 ✓ | ok |
| 129 | S129 | oui | ok (108, 120, 118) | 2 (0) ✓ | 0 / 2 / 55.9 | 0 fixes / 0 en chemin | - | ok |
| 130 | S130 | oui | ok (86, 120, 124) | 15 (0) ✓ | 0 / 15 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 131 | S131 | oui | ok (125, 153, 144) | 1 (0) ✓ | 0 / 1 / 55.3 | 0 fixes / 0 en chemin | - | ok |
| 132 | S132 | oui | ok (191, 157, 135) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 133 | S133 | oui | bleu (80, 163, 205) | 1 (1) ✓ | 0 / 1 / 3.1 | 0 fixes / 1 en chemin | 999999 → 999999 ✓ | ok |
| 134 | S134 | oui | ok (92, 58, 22) | 5 (6) ✓ | 0 / 5 / 3.1 | 6 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 135 | S135 | oui | ok (96, 121, 117) | 1 (0) ✓ | 0 / 1 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 136 | S136 | oui | ok (116, 117, 132) | 38 (38) ✓ | 1 / 37 / 3.1 | 38 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 137 | S137 | oui | bleu (119, 176, 200) | 3 (1) ✓ | 0 / 2 / 3.9 | 1 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 176 | S176 | oui | sombre (69, 1, 1) | 1 (2) ✓ | 0 / 1 / 3.1 | 2 fixes / 0 en chemin | 999999 → 999999 ✓ | ok |
| 177 | S177 | oui | bleu (89, 100, 154) | 63 (0) ✓ | 0 / 51 / 6.9 | 0 fixes / 0 en chemin | - | ok |
| 178 | S178 | oui | bleu (60, 71, 101) | 5 (0) ✓ | 0 / 5 / 70.6 | 0 fixes / 0 en chemin | - | ok |
| 179 | S179 | oui | bleu (127, 165, 192) | 6 (0) ✓ | 0 / 6 / 5.0 | 0 fixes / 0 en chemin | - | ok |
| 180 | S180 | oui | sombre (51, 26, 51) | 19 (27) ✓ | 0 / 19 / 0.0 | 27 fixes / 0 en chemin | 100000 → 100000 ✓ | ok |
| 181 | S181 | oui | ok (92, 105, 133) | 3 (41) ✓ | 0 / 2 / 1.0 | 41 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 182 | S182 | oui | ok (99, 102, 72) | 13 (4) ✓ | 0 / 12 / 0.0 | 4 fixes / 0 en chemin | 100000 → 999999 ✓ | ok |
| 183 | S183 | oui | ok (174, 212, 237) | 2 (9) ✓ | 0 / 2 / 0.0 | 9 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 184 | S184 | oui | ok (140, 69, 73) | 9 (30) ✓ | 0 / 9 / 3.1 | 25 fixes / 5 en chemin | 99999999 → 99999999 ✓ | ok |
| 185 | S185 | oui | ok (116, 111, 135) | 12 (25) ✓ | 0 / 12 / 3.1 | 23 fixes / 2 en chemin | 27012 → 27012 ✓ | ok |
| 186 | S186 | oui | ok (114, 69, 29) | 15 (17) ✓ | 0 / 14 / 2.5 | 17 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 187 | S187 | oui | ok (93, 33, 23) | 16 (23) ✓ | 0 / 16 / 3.1 | 23 fixes / 0 en chemin | 9999999 → 9999999 ✓ | ok |
| 188 |  | oui | ok (113, 138, 162) | 15 (0) ✓ | 0 / 15 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 189 |  | oui | ok (109, 120, 138) | 10 (10) ✓ | 1 / 9 / 2.9 | 10 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 190 |  | oui | bleu (25, 34, 116) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 191 |  | oui | bleu (68, 100, 173) | 4 (0) ✓ | 0 / 4 / 709.9 | 0 fixes / 0 en chemin | - | ok |
| 192 |  | oui | ok (147, 74, 82) | 8 (9) ✓ | 0 / 8 / 3.1 | 9 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 193 |  | oui | bleu (73, 197, 229) | 14 (2) ✓ | 0 / 14 / 3.1 | 2 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 194 |  | oui | ok (100, 66, 36) | 6 (41) ✓ | 0 / 6 / 3.1 | 37 fixes / 4 en chemin | 10269 → 10269 ✓ | ok |
| 195 | S195 | oui | bleu (78, 164, 230) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | absente |
| 196 | S196 | oui | ok (89, 124, 131) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | absente |
| 197 | S197 | oui | bleu (76, 163, 228) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | absente |
| 201 | S201 | oui | ok (151, 91, 3) | 17 (91) ✓ | 0 / 16 / 0.0 | 79 fixes / 12 en chemin | 103473 → 999999 ✓ | ok |
| 202 | S202 | oui | bleu (93, 129, 166) | 19 (98) ✓ | 0 / 17 / 0.0 | 84 fixes / 14 en chemin | 157151 → 999999 ✓ | ok |
| 203 | S203 | oui | bleu (90, 131, 165) | 15 (112) ✓ | 0 / 15 / 3.6 | 100 fixes / 12 en chemin | 225888 → 225888 ✓ | ok |
| 204 | S204 | oui | sombre (104, 2, 103) | 10 (90) ✓ | 0 / 10 / 0.0 | 76 fixes / 14 en chemin | 404816 → 404816 ✓ | ok |
| 205 | S205 | oui | ok (106, 28, 19) | 16 (113) ✓ | 0 / 16 / 3.1 | 96 fixes / 17 en chemin | 472982 → 472982 ✓ | ok |
| 206 | S206 | oui | bleu (8, 150, 210) | 14 (93) ✓ | 1 / 13 / 3.1 | 74 fixes / 19 en chemin | 549464 → 549464 ✓ | ok |
| 207 | S207 | oui | bleu (104, 145, 161) | 10 (112) ✓ | 0 / 10 / 1.6 | 95 fixes / 17 en chemin | 570003 → 570003 ✓ | ok |
| 208 | S208 | oui | bleu (6, 146, 205) | 10 (63) ✓ | 0 / 10 / 3.1 | 58 fixes / 5 en chemin | 963315 → 963315 ✓ | ok |
| 209 | S209 | oui | ok (78, 87, 101) | 4 (86) ✓ | 0 / 3 / 0.4 | 80 fixes / 6 en chemin | 996459 → 996459 ✓ | ok |
| 210 | S210 | oui | ok (246, 103, 103) | 8 (140) ✓ | 0 / 8 / 3.1 | 131 fixes / 9 en chemin | 1490913 → 1490913 ✓ | ok |
| 211 | S211 | oui | ok (70, 50, 51) | 22 (124) ✓ | 0 / 22 / 3.1 | 112 fixes / 12 en chemin | 1441628 → 1441628 ✓ | ok |
| 213 | S213 | oui | ok (126, 138, 159) | 4 (101) ✓ | 0 / 4 / 0.1 | 92 fixes / 9 en chemin | 1102414 → 1102414 ✓ | ok |
| 214 | S214 | oui | sombre (55, 7, 8) | 17 (64) ✓ | 0 / 17 / 3.1 | 53 fixes / 11 en chemin | 122143560 → 122143560 ✓ | ok |
| 215 | S215 | oui | ok (179, 214, 237) | 6 (109) ✓ | 0 / 6 / 3.1 | 95 fixes / 14 en chemin | 1394036 → 1394036 ✓ | ok |
| 217 | S217 | oui | ok (114, 124, 170) | 3 (47) ✓ | 0 / 3 / 3.1 | 44 fixes / 3 en chemin | 1706679 → 1706679 ✓ | ok |
| 218 | S218 | oui | ok (95, 111, 138) | 6 (59) ✓ | 0 / 6 / 3.1 | 45 fixes / 14 en chemin | 1765755 → 1765755 ✓ | ok |
| 219 | S219 | oui | bleu (3, 149, 204) | 9 (45) ✓ | 1 / 8 / 3.1 | 45 fixes / 0 en chemin | 1765755 → 1765755 ✓ | ok |
| 220 | S220 | oui | bleu (62, 96, 117) | 12 (52) ✓ | 0 / 12 / 3.1 | 51 fixes / 1 en chemin | 1957082 → 1957082 ✓ | ok |
| 221 | S221 | oui | bleu (7, 56, 91) | 1 (57) ✓ | 0 / 1 / 3.1 | 43 fixes / 14 en chemin | 1957082 → 1957082 ✓ | ok |
| 222 | S222 | oui | ok (70, 47, 80) | 8 (54) ✓ | 1 / 7 / 3.1 | 31 fixes / 23 en chemin | 2025990 → 2025990 ✓ | ok |
| 223 | S223 | oui | sombre (54, 16, 39) | 30 (84) ✓ | 0 / 30 / 0.0 | 61 fixes / 23 en chemin | 2097704 → 2097704 ✓ | ok |
| 225 | S225 | oui | ok (97, 53, 37) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 226 | S226 | oui | bleu (62, 77, 137) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 227 | S227 | oui | sombre (21, 20, 125) | 17 (159) ✓ | 0 / 16 / 0.0 | 153 fixes / 6 en chemin | 2347213 → 9999999 ✓ | ok |
| 284 | S284 | oui | ok (128, 121, 175) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 603 | S603 | oui | ok (99, 64, 35) | 6 (41) ✓ | 0 / 6 / 3.1 | 37 fixes / 4 en chemin | 555666 → 555666 ✓ | ok |
| 604 | S604 | oui | ok (119, 110, 104) | 6 (63) ✓ | 0 / 0 / 0.9 | 49 fixes / 14 en chemin | 694582 → 694582 ✓ | ok |
| 607 | S607 | oui | ok (34, 177, 168) | 5 (25) ✓ | 1 / 4 / 3.1 | 23 fixes / 2 en chemin | 675687 → 675687 ✓ | ok |
| 608 | S608 | oui | bleu (50, 50, 89) | 11 (33) ✓ | 0 / 11 / 3.1 | 29 fixes / 4 en chemin | 945963 → 945963 ✓ | ok |
| 612 | S612 | oui | ok (136, 109, 114) | 12 (36) ✓ | 0 / 12 / 3.1 | 29 fixes / 7 en chemin | 1017863 → 1017863 ✓ | ok |
| 613 | S613 | oui | ok (120, 98, 88) | 23 (56) ✓ | 0 / 22 / 0.7 | 49 fixes / 7 en chemin | 305360 → 1957082 ✓ | ok |
| 616 | S616 | oui | ok (187, 190, 144) | 8 (34) ✓ | 0 / 8 / 0.0 | 29 fixes / 5 en chemin | 1364328 → 1364328 ✓ | ok |
| 617 | S617 | oui | ok (244, 229, 196) | 14 (54) ✓ | 0 / 14 / 0.0 | 51 fixes / 3 en chemin | 1364328 → 1364328 ✓ | ok |
| 630 | S630 | oui | ok (89, 56, 30) | 6 (41) ✓ | 0 / 6 / 0.0 | 37 fixes / 4 en chemin | 2280117 → 2280117 ✓ | ok |
| 631 | S631 | oui | ok (117, 109, 103) | 6 (63) ✓ | 0 / 0 / 0.9 | 49 fixes / 14 en chemin | 2850146 → 2850146 ✓ | ok |
| 634 | S634 | oui | ok (34, 177, 168) | 5 (25) ✓ | 1 / 4 / 3.1 | 23 fixes / 2 en chemin | 1504877 → 1504877 ✓ | ok |
| 635 | S635 | oui | bleu (50, 51, 90) | 11 (33) ✓ | 0 / 11 / 3.1 | 29 fixes / 4 en chemin | 2280117 → 2280117 ✓ | ok |
| 639 | S639 | oui | ok (136, 109, 114) | 12 (36) ✓ | 0 / 12 / 3.1 | 29 fixes / 7 en chemin | 1710088 → 1710088 ✓ | ok |
| 640 | S640 | oui | ok (120, 98, 88) | 19 (56) ✓ | 0 / 19 / 0.7 | 49 fixes / 7 en chemin | 610745 → 610745 ✓ | ok |
| 649 | S649 | oui | ok (179, 214, 237) | 26 (126) ✓ | 0 / 26 / 3.1 | 115 fixes / 11 en chemin | 4413876 → 4413876 ✓ | ok |
| 650 | S650 | oui | bleu (114, 172, 207) | 26 (503) ✓ | 0 / 26 / 2.8 | 473 fixes / 30 en chemin | 4413876 → 4413876 ✓ | ok |
| 669 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 670 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 671 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 672 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 673 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 674 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 675 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 676 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 677 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 678 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 679 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 680 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 681 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 682 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 683 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 684 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 701 | S701 | oui | ok (89, 57, 30) | 6 (41) ✓ | 0 / 6 / 0.0 | 37 fixes / 4 en chemin | 2054 → 2054 ✓ | ok |
| 702 | S702 | oui | ok (116, 109, 103) | 6 (63) ✓ | 0 / 0 / 0.9 | 49 fixes / 14 en chemin | 3639 → 3639 ✓ | ok |
| 703 | S703 | oui | ok (34, 177, 168) | 5 (25) ✓ | 1 / 4 / 3.1 | 23 fixes / 2 en chemin | 5429 → 5429 ✓ | ok |
| 704 | S704 | oui | bleu (53, 52, 91) | 11 (33) ✓ | 0 / 11 / 3.1 | 29 fixes / 4 en chemin | 6266 → 6266 ✓ | ok |
| 705 | S705 | oui | ok (136, 109, 114) | 12 (37) ✓ | 0 / 12 / 3.1 | 29 fixes / 8 en chemin | 7656 → 7656 ✓ | ok |
| 706 | S706 | oui | ok (120, 98, 88) | 17 (62) ✓ | 0 / 17 / 0.7 | 55 fixes / 7 en chemin | 9215 → 9215 ✓ | ok |
| 707 | S707 | oui | ok (200, 204, 157) | 8 (35) ✓ | 0 / 8 / 3.1 | 30 fixes / 5 en chemin | 11561 → 11561 ✓ | ok |
| 708 | S708 | oui | bleu (54, 76, 119) | 22 (71) ✓ | 0 / 22 / 3.1 | 62 fixes / 9 en chemin | 12860 → 12860 ✓ | ok |
| 709 | S709 | oui | ok (115, 169, 157) | 5 (50) ✓ | 0 / 5 / 3.1 | 44 fixes / 6 en chemin | 14242 → 14242 ✓ | ok |
| 710 | S710 | oui | ok (95, 71, 82) | 12 (23) ✓ | 0 / 12 / 0.0 | 17 fixes / 6 en chemin | 20736 → 996459 ✓ | ok |
| 711 | S711 | oui | ok (115, 111, 139) | 12 (64) ✓ | 0 / 12 / 3.1 | 58 fixes / 6 en chemin | 18947 → 18947 ✓ | ok |
| 712 | S712 | oui | ok (220, 208, 178) | 14 (68) ✓ | 0 / 14 / 3.1 | 65 fixes / 3 en chemin | 41430 → 41430 ✓ | ok |
| 713 | S713 | oui | bleu (63, 49, 147) | 11 (79) ✓ | 0 / 11 / 3.1 | 69 fixes / 10 en chemin | 24577 → 24577 ✓ | ok |
| 714 | S714 | oui | ok (126, 116, 94) | 16 (82) ✓ | 0 / 0 / 0.4 | 74 fixes / 8 en chemin | 25615 → 25615 ✓ | ok |
| 715 | S715 | oui | ok (174, 175, 203) | 6 (54) ✓ | 0 / 6 / 3.1 | 45 fixes / 9 en chemin | 26685 → 26685 ✓ | ok |
| 716 | S716 | oui | ok (98, 89, 46) | 32 (64) ✓ | 1 / 31 / 3.1 | 58 fixes / 6 en chemin | 57830 → 945963 ✓ | ok |
| 717 | S717 | oui | ok (94, 79, 91) | 10 (39) ✓ | 0 / 10 / 3.1 | 35 fixes / 4 en chemin | 62560 → 62560 ✓ | ok |
| 718 | S718 | oui | ok (138, 68, 74) | 7 (37) ✓ | 0 / 7 / 3.1 | 31 fixes / 6 en chemin | 65028 → 65028 ✓ | ok |
| 719 | S719 | oui | ok (129, 148, 181) | 21 (54) ✓ | 0 / 21 / 1.0 | 50 fixes / 4 en chemin | 70184 → 70184 ✓ | ok |
| 720 | S720 | oui | bleu (55, 103, 146) | 12 (47) ✓ | 0 / 11 / 0.0 | 38 fixes / 9 en chemin | 72874 → 1957082 ✓ | ok |
| 721 | S721 | oui | ok (193, 190, 97) | 13 (58) ✓ | 0 / 13 / 0.0 | 52 fixes / 6 en chemin | 75644 → 75644 ✓ | ok |
| 764 | S764 | oui | ok (98, 91, 99) | 66 (0) ✓ | 5 / 57 / 3.0 | 0 fixes / 0 en chemin | - | ok |
| 765 | S765 | oui | ok (81, 69, 77) | 82 (112) ✓ | 0 / 81 / 0.0 | 112 fixes / 0 en chemin | 99999999 → 99999999 ✓ | ok |
| 766 | S766 | oui | bleu (106, 111, 223) | 27 (0) ✓ | 0 / 27 / 135.3 | 0 fixes / 0 en chemin | - | ok |
| 767 | S767 | oui | ok (69, 78, 69) | 28 (28) ✓ | 0 / 0 / 0.6 | 28 fixes / 0 en chemin | 792 → 792 ✓ | ok |
| 769 | S769 | oui | bleu (40, 73, 133) | 4 (4) ✓ | 0 / 4 / 3.1 | 4 fixes / 0 en chemin | 4413876 → 4413876 ✓ | ok |
| 770 | S770 | oui | ok (78, 87, 108) | 9 (0) ✓ | 0 / 9 / 10.9 | 0 fixes / 0 en chemin | - | ok |
| 771 |  | oui | ok (83, 42, 49) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 772 | S772 | oui | bleu (88, 118, 162) | 22 (92) ✓ | 0 / 22 / 3.1 | 92 fixes / 0 en chemin | 405178 → 405178 ✓ | ok |
| 773 |  | oui | ok (213, 243, 219) | 36 (0) ✓ | 3 / 27 / 2.5 | 0 fixes / 0 en chemin | - | ok |
| 775 | S775 | oui | ok (133, 164, 184) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 776 | S776 | oui | bleu (92, 129, 184) | 16 (43) ✓ | 0 / 12 / 2.5 | 38 fixes / 5 en chemin | 319482 → 0 ✓ | ok |
| 777 | S777 | oui | ok (144, 130, 133) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 778 | S778 | oui | ok (112, 96, 122) | 3 (0) ✓ | 0 / 3 / 73.1 | 0 fixes / 0 en chemin | - | ok |
| 779 | S779 | oui | ok (88, 77, 93) | 18 (61) ✓ | 0 / 18 / 3.1 | 33 fixes / 28 en chemin | 90000000 → 90000000 ✓ | ok |
| 780 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 781 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 782 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 783 | S783 | oui | ok (250, 234, 200) | 13 (17) ✓ | 0 / 13 / 3.1 | 13 fixes / 4 en chemin | 1 → 1 ✓ | ok |
| 784 | S784 | oui | ok (109, 48, 48) | 30 (66) ✓ | 1 / 29 / 3.1 | 9 fixes / 57 en chemin | 9999999 → 9999999 ✓ | ok |
| 785 | S785 | oui | ok (87, 73, 42) | 37 (63) ✓ | 0 / 37 / 4.4 | 56 fixes / 7 en chemin | 9999999 → 9999999 ✓ | ok |
| 786 | S786 | oui | ok (162, 144, 129) | 22 (2) ✓ | 0 / 0 / 6.7 | 2 fixes / 0 en chemin | 10 → 9999999 ✓ | ok |
| 787 | S787 | oui | ok (129, 165, 175) | 16 (1) ✓ | 0 / 14 / 0.1 | 0 fixes / 1 en chemin | 166710 → 99999999 ✓ | ok |
| 788 | S788 | oui | ok (36, 177, 172) | 6 (25) ✓ | 0 / 6 / 0.0 | 25 fixes / 0 en chemin | 12038 → 12038 ✓ | ok |
| 789 | S789 | oui | bleu (50, 70, 115) | 0 (0) ✓ | 0 / 0 / 0.0 | 0 fixes / 0 en chemin | - | ok |
| 790 | S790 | oui | bleu (55, 94, 150) | 1 (1) ✓ | 0 / 0 / 0.0 | 1 fixes / 0 en chemin | 150000 → 150000 ✓ | ok |
| 791 | S791 | oui | ok (115, 47, 50) | 15 (50) ✓ | 0 / 15 / 0.0 | 50 fixes / 0 en chemin | 16287 → 16287 ✓ | ok |
| 801 | S801 | oui | ok (89, 82, 88) | 8 (91) ✓ | 0 / 8 / 0.0 | 79 fixes / 12 en chemin | 10347 → 10347 ✓ | ok |
| 802 | S802 | oui | bleu (73, 104, 153) | 17 (98) ✓ | 0 / 16 / 0.0 | 84 fixes / 14 en chemin | 15715 → 15715 ✓ | ok |
| 803 | S803 | oui | bleu (106, 150, 184) | 15 (112) ✓ | 0 / 15 / 3.6 | 100 fixes / 12 en chemin | 22589 → 22589 ✓ | ok |
| 804 | S804 | oui | ok (99, 93, 123) | 10 (90) ✓ | 0 / 10 / 0.0 | 76 fixes / 14 en chemin | 57830 → 57830 ✓ | ok |
| 805 | S805 | oui | ok (80, 66, 76) | 16 (113) ✓ | 0 / 16 / 3.1 | 96 fixes / 17 en chemin | 67568 → 67568 ✓ | ok |
| 806 | S806 | oui | bleu (82, 112, 163) | 28 (93) ✓ | 0 / 27 / 0.0 | 74 fixes / 19 en chemin | 78494 → 2133349 ✓ | ok |
| 807 | S807 | oui | ok (110, 161, 173) | 11 (111) ✓ | 0 / 10 / 2.1 | 95 fixes / 16 en chemin | 81430 → 81430 ✓ | ok |
| 808 | S808 | oui | ok (141, 121, 182) | 7 (83) ✓ | 0 / 7 / 3.1 | 79 fixes / 4 en chemin | 90760 → 90760 ✓ | ok |
| 809 | S809 | oui | bleu (96, 106, 179) | 5 (64) ✓ | 0 / 5 / 3.1 | 63 fixes / 1 en chemin | 97452 → 97452 ✓ | ok |
| 810 | S810 | oui | ok (92, 36, 33) | 10 (53) ✓ | 1 / 9 / 3.1 | 49 fixes / 4 en chemin | 87560 → 87560 ✓ | ok |
| 811 | S811 | oui | bleu (124, 188, 230) | 8 (72) ✓ | 1 / 7 / 3.1 | 66 fixes / 6 en chemin | 94058 → 94058 ✓ | ok |
| 812 | S812 | oui | bleu (77, 110, 164) | 10 (63) ✓ | 0 / 10 / 3.1 | 58 fixes / 5 en chemin | 97452 → 97452 ✓ | ok |
| 813 | S813 | oui | ok (71, 82, 99) | 4 (86) ✓ | 0 / 3 / 0.4 | 80 fixes / 6 en chemin | 100948 → 100948 ✓ | ok |
| 814 | S814 | oui | ok (136, 147, 173) | 4 (101) ✓ | 0 / 4 / 0.1 | 92 fixes / 9 en chemin | 112080 → 112080 ✓ | ok |
| 815 | S815 | oui | sombre (55, 8, 10) | 17 (77) ✓ | 0 / 17 / 3.1 | 66 fixes / 11 en chemin | 122143560 → 122143560 ✓ | ok |
| 816 | S816 | oui | ok (228, 72, 83) | 23 (140) ✓ | 0 / 23 / 0.0 | 131 fixes / 9 en chemin | 159741 → 99999999 ✓ | ok |
| 817 | S817 | oui | ok (155, 125, 126) | 22 (123) ✓ | 0 / 22 / 0.0 | 112 fixes / 11 en chemin | 500000 → 500000 ✓ | ok |
| 818 | S818 | oui | ok (179, 214, 237) | 6 (108) ✓ | 0 / 6 / 3.1 | 95 fixes / 13 en chemin | 149361 → 149361 ✓ | ok |
| 819 | S819 | oui | bleu (137, 146, 248) | 3 (47) ✓ | 0 / 3 / 3.1 | 44 fixes / 3 en chemin | 121906 → 121906 ✓ | ok |
| 820 | S820 | oui | sombre (3, 21, 19) | 9 (56) ✓ | 0 / 9 / 3.1 | 52 fixes / 4 en chemin | 152382 → 152382 ✓ | ok |
| 821 | S821 | oui | bleu (88, 117, 161) | 8 (60) ✓ | 0 / 8 / 3.1 | 46 fixes / 14 en chemin | 99999999 → 99999999 ✓ | ok |
| 822 | S822 | oui | bleu (53, 132, 245) | 12 (44) ✓ | 1 / 11 / 3.1 | 44 fixes / 0 en chemin | 126125 → 76140 ✓ | ok |
| 823 | S823 | oui | bleu (51, 93, 151) | 12 (53) ✓ | 0 / 12 / 3.1 | 52 fixes / 1 en chemin | 139792 → 139792 ✓ | ok |
| 824 | S824 | oui | bleu (9, 69, 151) | 1 (57) ✓ | 0 / 1 / 3.1 | 43 fixes / 14 en chemin | 139792 → 139792 ✓ | ok |
| 825 | S825 | oui | ok (68, 57, 89) | 7 (54) ✓ | 0 / 7 / 3.1 | 31 fixes / 23 en chemin | 144714 → 144714 ✓ | ok |
| 826 | S826 | oui | ok (99, 38, 79) | 6 (83) ✓ | 0 / 6 / 0.0 | 60 fixes / 23 en chemin | 187295 → 9999999 ✓ | ok |
| 827 | S827 | oui | ok (118, 64, 47) | 21 (82) ✓ | 0 / 21 / 5.7 | 66 fixes / 16 en chemin | 157638 → 157638 ✓ | ok |
| 828 | S828 | oui | bleu (38, 49, 116) | 13 (100) ✓ | 0 / 13 / 0.1 | 96 fixes / 4 en chemin | 330262 → 330262 ✓ | ok |
| 829 | S829 | oui | ok (90, 57, 31) | 6 (41) ✓ | 0 / 6 / 0.0 | 37 fixes / 4 en chemin | 170200 → 170200 ✓ | ok |
| 830 | S830 | oui | ok (119, 110, 104) | 6 (63) ✓ | 0 / 0 / 0.9 | 49 fixes / 14 en chemin | 170200 → 170200 ✓ | ok |
| 831 | S831 | oui | ok (34, 177, 168) | 5 (25) ✓ | 1 / 4 / 3.1 | 23 fixes / 2 en chemin | 170200 → 170200 ✓ | ok |
| 832 | S832 | oui | bleu (53, 52, 91) | 11 (33) ✓ | 0 / 11 / 3.1 | 29 fixes / 4 en chemin | 170200 → 170200 ✓ | ok |
| 833 | S833 | oui | ok (136, 109, 114) | 12 (37) ✓ | 0 / 12 / 3.1 | 29 fixes / 8 en chemin | 170200 → 170200 ✓ | ok |
| 834 | S834 | oui | ok (120, 98, 88) | 16 (62) ✓ | 0 / 16 / 0.7 | 55 fixes / 7 en chemin | 170200 → 170200 ✓ | ok |
| 835 | S835 | oui | ok (89, 81, 87) | 8 (91) ✓ | 0 / 8 / 3.1 | 79 fixes / 12 en chemin | 170200 → 170200 ✓ | ok |
| 836 | S836 | oui | ok (200, 204, 157) | 12 (35) ✓ | 0 / 12 / 3.1 | 30 fixes / 5 en chemin | 170200 → 1957082 ✓ | ok |
| 837 | S837 | oui | bleu (54, 78, 122) | 22 (71) ✓ | 0 / 22 / 3.1 | 62 fixes / 9 en chemin | 170200 → 170200 ✓ | ok |
| 838 | S838 | oui | bleu (75, 108, 160) | 17 (98) ✓ | 1 / 16 / 3.1 | 84 fixes / 14 en chemin | 170200 → 170200 ✓ | ok |
| 839 | S839 | oui | ok (115, 111, 139) | 12 (64) ✓ | 0 / 12 / 3.1 | 58 fixes / 6 en chemin | 170200 → 170200 ✓ | ok |
| 840 | S840 | oui | ok (222, 210, 180) | 14 (68) ✓ | 0 / 14 / 3.1 | 65 fixes / 3 en chemin | 170200 → 170200 ✓ | ok |
| 841 | S841 | oui | bleu (105, 150, 184) | 15 (112) ✓ | 0 / 15 / 3.6 | 100 fixes / 12 en chemin | 170200 → 170200 ✓ | ok |
| 842 | S842 | oui | bleu (63, 49, 147) | 11 (79) ✓ | 0 / 11 / 3.1 | 69 fixes / 10 en chemin | 170200 → 170200 ✓ | ok |
| 843 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 844 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 845 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 846 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 847 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 848 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 849 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 850 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 851 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 852 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 853 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 854 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 855 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 856 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 857 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 858 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 901 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 902 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 903 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 994 |  | AUCUNE SCÈNE | - | - | - | - | - | - |
| 995 |  | AUCUNE SCÈNE | - | - | - | - | - | - |

---

# Balayage chemins d'entrée (2026-11-06) — statut `verified` (client live Godot 4.8-beta2)

Sweep des **portails d'entrée** sur client réel connecté : marcher dans
l'aire gateway → offre 9060 → choix solo 9061 → migration. Preuves :
`[Dungeon] offre/entrée` + `MigrationDone` dans le log zone + captures.

## Portails Solo/Multi (paires 7xx/8xx ↔ 1xx)

**44 paires validées bout-en-bout** (offre → entrée solo →
`MigrationDone`) : 701, 702, 703, 706, 707, 708, 709, 710, 711, 712,
714, 715, 716, 717, 718, 719, 720, 721, 801, 802, 803, 804, 805, 806,
807, 808, 809, 810, 811, 812, 813, 814, 816, 817, 818, 819, 820, 821,
823, 824, 825, 826 + 104/704 et 105/705 (chaîne Goss, entrée solo ET
multi prouvées par la quête 1878).

### Bug corrigé : aires gateway imbriquées (`MissionBridge.WatchGateways`)

Le suivi de portail était *first-match* : la première aire gateway
contenant le joueur « gagnait » et masquait toutes les autres. Sur
S007 le portail Brandroot (aire 24, r=3) est **à l'intérieur** de
l'aire 10 (r=10) et Dark Palace (aire 42, r=2) dans l'aire 14 (r=20) ;
sur S009 Geothermal Rift (aire 27, r=3) dans l'aire 5 (r=20). Un vrai
joueur n'aurait **jamais** pu entrer dans ces donjons — le fix suit un
front montant **par aire** au lieu du premier match. Revalidé live :
offre + entrée + migration sur les trois.

Attention au balayage : l'arrivée seed les aires déjà occupées —
s'approcher depuis **hors du rayon** (r=2..10 selon le portail) puis
marcher dedans, sinon pas de front montant (comportement correct).

### Fenêtre d'offre — fermeture

La fenêtre « Portail de donjon » surveille `PortalArrival.Version` en
`_Process` : migration, `//goto`, téléport et revive la ferment —
resynchronisée à l'ouverture. Validé visuellement (Flameshot avant/
après teleport).

## Nodes « campagne » (type 1, `Event_Transport` direct, pas d'offre)

| node | portail | conditions | résultat |
|---|---|---|---|
| 212 密境研究 | S032 (581.2,188.9) r=3 | `Event_If_Level 71` | `MigrationDone node=212` à niv.85 |
| 216 毀滅次�的 | S034 (397.2,79.3) r=3 | `Event_If_Level 74` | `MigrationDone node=216` à niv.85 |
| 224 時空裂縫 | S053 (363.2,129.7) r=4 | `Event_If_Level 83` + `Event_If_Monster 197` | niveau OK à 85 ; transport refusé car l'état monstre 197 (boss événement monde) n'est pas actif — **refus natif correct**, pas un défaut |

## Nodes multi-only (【多人強度】 sans paire 單人強度）

126 怒目神廟, 225 巴特羅山谷, 226 亞露蘭伊斯, 227 沙夏聖境, 232
怒心魔殿 (et d'autres : 110-122, 131-132, 153-156, 202-208, 217, 220…)
**n'ont aucun portail monde** (`Event_Transport` absent de toute
scène). Leur accès natif = service NPC **type 35 `instance_dialog`** :

```
S_Npc #:Service <dlgId> 35 <refId> <nodeId>
→ serveur envoie 564 ClientOpenInstanceDialog [s16 refId][s16 nodeId]
```

- Serveur : 564 émis correctement — **prouvé live** : NPC 50055
  (S007, 55.7,126.4, devant Dark Palace) → `E NCID=564 [8][112]`,
  conditions `If_CharLevel >= 30` (souvent aussi `If_Raid team` /
  `If_Colosseum 0` pour les entrées raid, ex. 迪亞娜女爵 → [64][126]).
- Client : `FeatureDialogWindow` n'affiche qu'un stub texte — le flux
  natif `InstanceDungeonControl` (création/confirmation d'instance) est
  **non implémenté** → entrée impossible pour l'instant. C'est un
  **gap de feature** (système d'instances/versus), pas un bug des
  données. Paquet de confirmation C→S non identifié dans le natif à ce
  stade (pas de `ServerInstanceDialog` dans zone ; piste =
  recrutement/`CVersusInstanceFactory`).

## Notes

- Passe dev `EDEN_DEV_DUNGEON_PASS` : entrées illimitées, non persisté.
- Le catalogue `DungeonCatalog` n'inscrit un node que si sa paire solo
  existe (`SoloMarker` 單人強度) — les multi-only sont donc absents du
  catalogue, ce qui est cohérent avec l'absence d'offre.
- Sortie de donjon = retour au savepoint natif (prouvé S705→S005,
  S105→S002 eject post-mort).
