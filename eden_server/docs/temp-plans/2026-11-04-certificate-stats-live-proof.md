# Plan — preuve live stats certificats (2026-11-04)

Symptôme signalé : « les stats des certificats de classe ne s'appliquent
pas — aucune modif visible dans le panneau profil ».

## État de l'audit statique (fait)
- Chaîne serveur complète vérifiée : `player_license` → `LicenseService.LoadAsync`
  (slot/enchant) → stage `"licenses"` (Program.cs) → `StatPipeline.ResolveAsync`
  → `ProfileStatEmitter.SendAsync` (296/492/493) → `CombatantSnapshot.
  ApplyContribution`.
- Handler 182 (classe courante) : SetCurrentClassLicenseAsync → écho 509
  → ResolveWeapon → SendStatBlocksAsync. Correct.
- Client : `ApplyAttrRefresh` (296) → `PlayerVitals.TotalStats` →
  `ProfileWindow.RefreshStats` — aucun recalcul local. Correct.
- **Cause probable du symptôme : les 4 certs de test123 (char 200000001)
  ont loc=0 (possédés, jamais posés) + les 4 enchants sont faibles/invisibles
  en profil (2122 hate, 2083 DEF+5%, 2085 WIS+10%, 2085 LCK+10%).**

## Preuve live planifiée (headless, char 200000002 — pas de conflit session)
1. Baseline : eden_session.py char 200000002 → capter 296/492/493 + 509.
2. Seed DB : `player_license (200000002, 24235, 0)` — cert MP+5%
   (item 24235 → enchant 46304 `1014|5`, visible dans 296 field16 i32 MaxMP).
3. Login → 296 identique (possédé non posé = pas de stats — le contrôle).
4. Envoyer 182 `{24235→slot1}` → nouveau 296 (MaxMP +5%) + écho 509 loc=1.
5. Nettoyer la ligne seedée.

## Réalisé (2026-11-03 soir — CLOS)

- **Headless TLS `HeadlessBot` (char 200000002)** — cert 33273
  (J502 DEF+5 %) : `509 33273:loc0` → 182 slot1 → `509 loc1` +
  296 `DEF 541→568` (=541×1,05 exact) → 182 slot0 → `loc0` +
  `DEF 541`. ✅ wire complet baseline→activation→désactivation.
- **Cause racine confirmée** : zone tournait un binaire supprimé
  pré-`4839e2e` (restart `start-native.sh` 22:41) + les certs du
  perso étaient loc=0 (non posés — règle native vérifiée).
- **Client graphique (char 200000001, ~23:00)** — `--hud-open=profile`
  + `--shot` : fenêtre « Character Info » rendue (diff région 62-72 %
  vs capture sans fenêtre), 296 décodé :
  DEF 1016→1067 (+5 % exact), WIS 161→177 (+10 %), INT 141→154,
  M-ATK 1252→1320, STR/AGI/LCK/P-ATK inchangés ; retour loc=0 →
  valeurs d'origine. ✅ pixel + wire sur le vrai client.
- Artefact élucidé : Block 0↔393 = debuff joueur enchant 52153
  (`2117|−200`) recasté en continu — hors certificats (preuve que le
  pipeline buffs marche aussi en live).

## Limites
- ~~Le perso test123 est en ligne — pas de preuve pixel~~ → levée :
  captures `/tmp/eden_profile_{before,after,closed,after2}.png`.
- Deltas petits sur un perso lv1 (cert % uniquement dans les données FR).
- Seeds DB laissés : aucun (loc du perso restauré à 0 ; une 5ᵉ licence
  33274 gagnée par le jeu en cours est restée loc=0 — état authentique).
