using Eden.Core.Game.Classes;
using Eden.Core.Database.Repositories;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service gérant le changement de classe et la progression
/// Correspond à CPlayerFactory::ChangeClass et fonctions associées
/// </summary>
public sealed class ClassChangeService
{
    private readonly IClassRepository _classRepository;
    private readonly IReadOnlyList<ClassEntity> _allClasses;
    
    public ClassChangeService(IClassRepository classRepository, IReadOnlyList<ClassEntity> allClasses)
    {
        _classRepository = classRepository;
        _allClasses = allClasses;
    }
    
    /// <summary>
    /// Tente de changer la classe active du personnage
    /// Correspond à CPlayerFactory::ChangeClass
    /// </summary>
    public async Task<(bool Success, string ErrorMessage)> ChangeClassAsync(
        int characterId, 
        int targetClassId,
        int currentCharacterLevel,
        IReadOnlyList<ClassProgressEntity> currentClasses)
    {
        // 1. Vérifier que la classe existe
        var targetClass = _allClasses.FirstOrDefault(c => c.Id == targetClassId);
        if (targetClass == null)
        {
            return (false, "Class not found");
        }
        
        // 2. Vérifier que le joueur a débloqué cette classe
        var classProgress = currentClasses.FirstOrDefault(c => c.ClassId == targetClassId);
        if (classProgress == null)
        {
            return (false, "Class not unlocked");
        }
        
        // 3. Vérifier les prérequis de niveau
        if (currentCharacterLevel < targetClass.RequiredCharacterLevel)
        {
            return (false, $"Character level {targetClass.RequiredCharacterLevel} required");
        }
        
        // 4. Vérifier les prérequis de classe parente
        if (targetClass.RequiredClassId > 0)
        {
            var parentProgress = currentClasses.FirstOrDefault(c => c.ClassId == targetClass.RequiredClassId);
            if (parentProgress == null || parentProgress.ClassLevel < targetClass.RequiredClassLevel)
            {
                return (false, $"Parent class {targetClass.RequiredClassId} level {targetClass.RequiredClassLevel} required");
            }
        }
        
        // 5. Vérifier les prérequis de quête
        if (!string.IsNullOrEmpty(targetClass.RequiredQuest))
        {
            // TODO: Vérifier que la quête est complétée
            // Pour l'instant, on assume que c'est validé par ailleurs
        }
        
        // 6. Effectuer le changement
        var success = await _classRepository.ChangeClassAsync(characterId, targetClassId);
        if (!success)
        {
            return (false, "Failed to update character class");
        }
        
        return (true, string.Empty);
    }
    
    /// <summary>
    /// Ajoute une nouvelle classe au joueur
    /// Correspond à CPlayerFactory::AddClass
    /// </summary>
    public async Task<int> UnlockClassAsync(int characterId, int classId)
    {
        // Vérifier que la classe n'est pas déjà débloquée
        var existing = await _classRepository.GetClassProgressAsync(characterId, classId);
        if (existing != null)
        {
            return existing.Id; // Déjà débloquée
        }
        
        return await _classRepository.AddClassAsync(characterId, classId);
    }
    
    /// <summary>
    /// Calcule l'XP requise pour le niveau suivant d'une classe
    /// Correspond à CLevelDBO::LoadClassLevelExp
    /// </summary>
    public long GetExpForNextLevel(int classId, int currentLevel)
    {
        // Formule de base (à affiner avec les données de S_Class.ini)
        return (long)Math.Pow(currentLevel, 2) * 100;
    }
}
