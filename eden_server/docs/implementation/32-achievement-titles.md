# 32 - Succès et Titres

> **Status:** 🟡 En cours — données chargées  
> **Priorité:** MOYENNE  
> **Complexité:** Moyenne

---

## 1. Description

3 844 succès + 337 titres avec bonus de stats.

## 2. Données

### S_Achievement.ini (`|V.4|15|`, 3 844 entrées)
[0] id, [2] type, [3] code (M025), [4] nom, [5-8] paramètres
(seuil 999...), [14] description condition.

### S_Title.ini (`|V.3|6|`, 337 entrées)
[0] id, [1] nom, [2] type (一般稱號), [3] **spell id du bonus** (53090),
[4] description du bonus (`敏捷屬性提高 5 點` = +5 AGI),
[5] condition d'obtention.

## 3. Implémentation C#

- `GameDataParsers.ParseAchievements()` — `AchievementDef`
- `GameDataParsers.ParseTitles()` — `TitleDef` (avec `SpellId` du bonus)

## 4. TODO / À reverser

- Moteur de déblocage : relier aux événements (MissionService/combat)
- Application du spell bonus du titre équipé (via SpellService)
- Persistance `character_titles` / `character_achievements`
