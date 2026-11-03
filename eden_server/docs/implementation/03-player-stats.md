# 03 - Stats et Profil Joueur

> **Status:** 🟡 En cours d'implémentation  
> **Priorité:** MAX (Fondation du jeu)  
> **Complexité:** Moyenne

---

## 1. Description du Système

Le système de stats gère:
- Les 6 attributs principaux: STR, DEX, CON, INT, PSY, AGI
- Les HP/MP et leur régénération
- Les formules de calcul de stats par niveau
- Les breakpoints (paliers) d'optimisation
- Les stats de base par race et classe
- Les buffs et debuffs temporaires

### Attributs Principaux
- **STR (Strength)** - Influence les dégâts physiques
- **DEX (Dexterity)** - Influence la précision et esquive
- **CON (Constitution)** - Influence les HP et défense
- **INT (Intelligence)** - Influence les dégâts magiques
- **PSY (Psyche)** - Influence les MP et résistance magique
- **AGI (Agility)** - Influence la vitesse d'attaque et esquive

### Stats Dérivées
- **HP Max** - Calculé depuis CON et niveau
- **MP Max** - Calculé depuis PSY et niveau
- **Physical Attack** - Calculé depuis STR et arme
- **Magic Attack** - Calculé depuis INT et arme
- **Physical Defense** - Calculé depuis CON et armure
- **Magic Defense** - Calculé depuis PSY et armure
- **Critical Rate** - Influencé par DEX et AGI
- **Dodge Rate** - Influencé par AGI

---

## 2. Symboles lapis:: Analysés

### Character Stats
- `CCharacter` - Classe principale du personnage
- `CCharacter::GetSTR`, `GetDEX`, `GetCON`, `GetINT`, `GetPSY`, `GetAGI` - Getters d'attributs
- `CCharacter::GetMaxHP`, `GetMaxMP` - Getters HP/MP max
- `CCharacter::GetCurrentHP`, `GetCurrentMP` - Getters HP/MP actuels
- `CCharacter::GetPhysicalAttack`, `GetMagicAttack` - Attaques physiques/magiques
- `CCharacter::GetPhysicalDefense`, `GetMagicDefense` - Défenses

### Formulas
- `CCharFormula` - Classe de formules de combat
- `CCharFormula::CalcMaxHP` - Calcul HP max
- `CCharFormula::CalcMaxMP` - Calcul MP max
- `CCharFormula::CalcPhysicalAttack` - Calcul attaque physique
- `CCharFormula::CalcMagicAttack` - Calcul attaque magique
- `CCharFormula::CalcPhysicalDefense` - Calcul défense physique
- `CCharFormula::CalcMagicDefense` - Calcul défense magique
- `CCharFormula::CalcCriticalRate` - Calcul taux critique
- `CCharFormula::CalcDodgeRate` - Calcul taux esquive

### Level Progression
- `CLevelDBO` - Database Object des niveaux
- `CLevelDBO::LoadLevelExp` - Chargement XP par niveau
- `CLevelDBO::LoadBaseStats` - Chargement stats de base par niveau
- `CLevelDBO::LoadClassLevelExp` - Chargement XP classe par niveau

### Buff/Debuff Effects
- `CEC_AttrResist` - Résistance aux attributs
- `CEC_AttrShield` - Bouclier d'attribut
- `CEC_DamageRate` - Taux de dégâts
- `CEC_HPMaxRate` - Taux HP max
- `CEC_MPMaxRate` - Taux MP max
- `CEC_Regenerate` - Régénération HP/MP

---

## 3. Fichiers de Données Associés

### Stats par Niveau
- `S_Class.ini` - Stats de base par classe et niveau (encodé MyCrypt)
- `S_ClassRankBuff.ini` - Buffs par rang de classe
- `S_ClassDropItem.ini` - Items drop par classe

### Progression
- `S_LevelExp.ini` - XP requise par niveau de personnage
- `S_ClassLevelExp.ini` - XP requise par niveau de classe

### Stats de Race
- `S_Races.ini` - Bonus/malus par race

---

## 4. Protocole Réseau

### CNC_CZ_ClientStatsUpdate (Zone → Client)
**Champs:**
- `CharacterId` (int)
- `CurrentHP` (int)
- `CurrentMP` (int)
- `MaxHP` (int)
- `MaxMP` (int)
- Stats dérivées

### CNC_CZ_ClientUpdateAttributes (Zone → Client)
**Champs:**
- `STR` (short)
- `DEX` (short)
- `CON` (short)
- `INT` (short)
- `PSY` (short)
- `AGI` (short)

---

## 5. Base de Données PostgreSQL

### Table existante: player_characters
La table contient déjà:
- `level` - Niveau du personnage
- `exp` - XP du personnage
- `hp`, `max_hp` - HP actuel et max
- `mp`, `max_mp` - MP actuel et max
- `combo_points` - Points de combo

### Table à ajouter: character_stats
```sql
CREATE TABLE character_stats (
    character_id INTEGER PRIMARY KEY REFERENCES player_characters(id),
    str_base INTEGER DEFAULT 0,
    dex_base INTEGER DEFAULT 0,
    con_base INTEGER DEFAULT 0,
    int_base INTEGER DEFAULT 0,
    psy_base INTEGER DEFAULT 0,
    agi_base INTEGER DEFAULT 0,
    str_bonus INTEGER DEFAULT 0,
    dex_bonus INTEGER DEFAULT 0,
    con_bonus INTEGER DEFAULT 0,
    int_bonus INTEGER DEFAULT 0,
    psy_bonus INTEGER DEFAULT 0,
    agi_bonus INTEGER DEFAULT 0
);
```

---

## 6. Implémentation C#

### 6.1 Entités

#### PlayerStatsEntity.cs
```csharp
namespace Eden.Core.Game.Stats;

/// <summary>
/// Stats d'un personnage (base + bonus)
/// </summary>
public sealed class PlayerStatsEntity
{
    public int CharacterId { get; set; }
    
    // Base stats (déterminées par race, classe, niveau)
    public int STR_Base { get; set; }
    public int DEX_Base { get; set; }
    public int CON_Base { get; set; }
    public int INT_Base { get; set; }
    public int PSY_Base { get; set; }
    public int AGI_Base { get; set; }
    
    // Bonus stats (équipement, buffs, talents)
    public int STR_Bonus { get; set; }
    public int DEX_Bonus { get; set; }
    public int CON_Bonus { get; set; }
    public int INT_Bonus { get; set; }
    public int PSY_Bonus { get; set; }
    public int AGI_Bonus { get; set; }
    
    // Stats calculées (readonly)
    public int STR_Total => STR_Base + STR_Bonus;
    public int DEX_Total => DEX_Base + DEX_Bonus;
    public int CON_Total => CON_Base + CON_Bonus;
    public int INT_Total => INT_Base + INT_Bonus;
    public int PSY_Total => PSY_Base + PSY_Bonus;
    public int AGI_Total => AGI_Base + AGI_Bonus;
}
```

### 6.2 Service de Stats

#### StatsCalculationService.cs
```csharp
namespace Eden.Core.Game.Services;

/// <summary>
/// Service de calcul des stats et formules
/// Correspond à CCharFormula
/// </summary>
public sealed class StatsCalculationService
{
    /// <summary>
    /// Calcule les HP max pour un personnage
    /// Correspond à CCharFormula::CalcMaxHP
    /// </summary>
    public int CalculateMaxHP(int level, int con, int classHpMod)
    {
        // Formule de base (à affiner avec S_Class.ini)
        return 100 + (level * 10) + (con * 5) + classHpMod;
    }
    
    /// <summary>
    /// Calcule les MP max pour un personnage
    /// Correspond à CCharFormula::CalcMaxMP
    /// </summary>
    public int CalculateMaxMP(int level, int psy, int classMpMod)
    {
        // Formule de base (à affiner avec S_Class.ini)
        return 50 + (level * 5) + (psy * 3) + classMpMod;
    }
    
    /// <summary>
    /// Calcule l'attaque physique
    /// Correspond à CCharFormula::CalcPhysicalAttack
    /// </summary>
    public int CalculatePhysicalAttack(int str, int weaponDamage, int level)
    {
        return str * 2 + weaponDamage + (level / 2);
    }
    
    /// <summary>
    /// Calcule l'attaque magique
    /// Correspond à CCharFormula::CalcMagicAttack
    /// </summary>
    public int CalculateMagicAttack(int intStat, int weaponDamage, int level)
    {
        return intStat * 2 + weaponDamage + (level / 2);
    }
    
    /// <summary>
    /// Calcule la défense physique
    /// Correspond à CCharFormula::CalcPhysicalDefense
    /// </summary>
    public int CalculatePhysicalDefense(int con, int armorDefense, int level)
    {
        return con + armorDefense + (level / 3);
    }
    
    /// <summary>
    /// Calcule la défense magique
    /// Correspond à CCharFormula::CalcMagicDefense
    /// </summary>
    public int CalculateMagicDefense(int psy, int armorDefense, int level)
    {
        return psy + armorDefense + (level / 3);
    }
    
    /// <summary>
    /// Calcule le taux critique
    /// Correspond à CCharFormula::CalcCriticalRate
    /// </summary>
    public double CalculateCriticalRate(int dex, int agi, int critBonus)
    {
        // 5% base + DEX/100 + AGI/200
        return 0.05 + (dex / 100.0) + (agi / 200.0) + (critBonus / 100.0);
    }
    
    /// <summary>
    /// Calcule le taux d'esquive
    /// Correspond à CCharFormula::CalcDodgeRate
    /// </summary>
    public double CalculateDodgeRate(int agi, int dodgeBonus)
    {
        // 5% base + AGI/150
        return 0.05 + (agi / 150.0) + (dodgeBonus / 100.0);
    }
}
```

---

## 7. Tests de Validation

### StatsCalculationTests.cs
```csharp
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class StatsCalculationTests
{
    private readonly StatsCalculationService _service = new();
    
    [Fact]
    public void CalculateMaxHP_IncludesConBonus()
    {
        var hp1 = _service.CalculateMaxHP(10, 10, 0);
        var hp2 = _service.CalculateMaxHP(10, 20, 0);
        
        Assert.True(hp2 > hp1);
    }
    
    [Fact]
    public void CalculateCriticalRate_IncludesDexAndAgi()
    {
        var crit1 = _service.CalculateCriticalRate(10, 10, 0);
        var crit2 = _service.CalculateCriticalRate(20, 20, 0);
        
        Assert.True(crit2 > crit1);
    }
}
```

---

## 8. À Faire (TODO)

- [ ] Parser S_Class.ini pour les stats réelles par classe/niveau
- [ ] Parser S_Races.ini pour les bonus de race
- [ ] Implémenter les breakpoints d'optimisation
- [ ] Créer la table character_stats dans PostgreSQL
- [ ] Implémenter les handlers réseau pour mise à jour des stats
- [ ] Tests d'intégration complets

---

## 9. Notes de Reverse Engineering

- Les formules sont stockées dans CCharFormula et utilisent des valeurs entières
- Les breakpoints sont des paliers où les bonus augmentent de manière significative
- Les buffs/debuffs modifient les stats bonus, pas les stats de base
- La régénération HP/MP est gérée par des effects CEC_Regenerate
- Les stats de race sont des bonus/malus appliqués à la création du personnage

---

*Document généré le 15 Octobre 2026*
