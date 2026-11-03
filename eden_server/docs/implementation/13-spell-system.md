# 13 - Système de Sorts

> **Status:** 🟡 En cours — catalogue chargé, moteur de buffs livré  
> **Priorité:** CRITIQUE  
> **Complexité:** Élevée

---

## 1. Description

Catalogue complet des sorts + gestion des buffs actifs sur les entités.

## 2. Symboles lapis::

- `CSpellFactory`, `CSpellMasterFactory` — fabriques de sorts
- `CSpellTemplate`, `SSpellData` — templates/données
- `CCharacter::ClearAllTeamSpell` — nettoyage des buffs de groupe

## 3. Données : S_Spell.ini (`|V.8|45|`, 4 485 sorts)

| Col | Contenu |
|-----|---------|
| 0 | Spell ID (50001...) |
| 1 | Code (J001...) |
| 15 | Catégorie ('O' = buff/passif) |
| 16 | Nom (Big5) |
| 17 | Type |
| 18 | Type de cible |
| 19-21 | Paramètres |
| 33 | Durée |
| 39 | Spell résultat/associé |
| 40 | Valeur |

Les sorts par classe proviennent de `S_Class.ini` (cols 30-54) et les sorts
raciaux des cols 22-27 — déjà extraits par `ClassStatsParser`.

## 4. Implémentation C#

- `Eden.Core/Data/Parsers/SpellParser.cs` — `SpellDef`
- `Eden.Core/Game/Services/SpellService.cs`
  - `ApplySpell(spellId, caster, target)` — buffs stackés si catégorie 'O'
    ou durée > 0
  - `ClearAllBuffs(target)` = `ClearAllTeamSpell`
  - `GetBuffs(target)`, `Tick(target)` — expiration par ticks
  - `LoadFromDataDir(decodedDir)`

## 5. Tests

`SpellParserTests` : 4 485 sorts chargés, stacking de buffs, nettoyage.

## 6. TODO / À reverser

- Cooldowns (cols non mappées de S_Spell)
- Consommation MP/SP, portée, temps de cast
- Types de dégâts (physique/magique/élémentaire)
- CSpellTemplate : hiérarchie de templates à reverser
