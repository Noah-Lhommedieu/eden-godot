using Eden.Core.Game.Classes;

namespace Eden.Core.Database.Repositories;

public interface IClassRepository
{
    Task<IReadOnlyList<ClassProgressEntity>> GetClassesByCharacterIdAsync(int characterId);
    Task<ClassProgressEntity?> GetClassProgressAsync(int characterId, int classId);
    Task<int> AddClassAsync(int characterId, int classId);
    /// <summary>Migration KB-04 : éveillées historiques lv1/aa1 → 80/80
    /// (idempotent). Retourne le nombre de lignes corrigées.</summary>
    Task<int> PromoteAwakenedAsync();
    Task<bool> ChangeClassAsync(int characterId, int newClassId);
    Task AddClassExpAsync(int characterId, int classId, long expGain);
    /// <summary>+N aa_point sur le pool de la classe COURANTE
    /// (GainAAPoint natif @0x840c7f0 — player_characters.aa_point,
    /// cap 0x78=120, +1 par niveau de classe). Retourne le nouveau
    /// total (-1 si perso absent).</summary>
    Task<int> AddAaPointAsync(int characterId, int points);
    /// <summary>Dépense atomique de aa_point sur la ligne
    /// player_classlist d'une AUTRE classe (AddAAEffectPoint param_2!=0)
    /// — retourne le solde restant ou -1 si insuffisant.</summary>
    Task<int> TrySpendAaPointAsync(int characterId, int classId,
        int cost);
    /// <summary>Dépense atomique sur le pool de la classe courante
    /// (AddAAEffectPoint param_2==0 → player_characters.aa_point)
    /// — retourne le solde restant ou -1 si insuffisant.</summary>
    Task<int> TrySpendCurrentAaPointAsync(int characterId, int cost);
    /// <summary>Bascule du pool aa_point au changement de classe
    /// (PlayerFactory natif : range le pool dans l'ancienne ligne
    /// classlist, recharge celui de la nouvelle). Retourne le pool de
    /// la nouvelle classe (-1 si absent).</summary>
    Task<int> SwapAaPoolOnClassChangeAsync(int characterId,
        int oldClassId, int newClassId);
    /// <summary>+N skill points — retourne le nouveau total (GainNp
    /// natif émet la valeur absolue via ClientUpdateAttr 0x3ef).</summary>
    Task<int> AddClassPointsAsync(int characterId, int classId, int points);
    /// <summary>Spellmasters appris (player_spells) — ids en s16 signé
    /// natif (50017 → -15535) ; <c>Level</c> = colonne spell_level
    /// (spell_order est la position dans la barre, JAMAIS le niveau —
    /// le wire 311 écrit CSpellMaster+0x20 = level).</summary>
    Task<IReadOnlyList<(short Spell, short Level)>> GetPlayerSpellsAsync(int characterId);
    /// <summary>Apprend/améliore un spellmaster : INSERT ou spell_level++
    /// (SQL natif GetSpellMasterInsert/UpdateSQLCmd @0x863a790).
    /// Retourne le nouveau niveau de sort (0 = échec).</summary>
    Task<int> LearnSpellAsync(int characterId, short spellmasterId);

    /// <summary>Variante batch de <see cref="LearnSpellAsync"/> pour
    /// l'octroi d'entrée (Phase 2 — pipeline hors-boucle) : 1 SELECT
    /// des ids déjà appris (spell_level++ groupé), 1 COUNT pour la
    /// base spell_order, 1 INSERT multi-lignes via unnest — au lieu de
    /// 3 requêtes par sort.</summary>
    Task LearnSpellsAsync(int characterId,
        IReadOnlyList<short> spellmasterIds);

    /// <summary>Fixe `spell_level` à une valeur exacte (btn_max :
    /// montée multi-niveaux en une seule écriture — miroir de la
    /// boucle `SetSpellLevel` native).</summary>
    Task SetSpellLevelAsync(int characterId, short spellmasterId,
        short level);
    /// <summary>Réécrit lic_1..lic_7 d'une classe — layout de
    /// certificats sauvegardé (`SetOtherClassLicense` natif
    /// @0x82717f0). 0 = slot vide.</summary>
    Task SetClassLicensesAsync(int characterId, int classId,
        int[] licenses);

    /// <summary>Slots de garde-robe du perso (`player_closet` — SELECT
    /// natif <c>CExecutionerDBO::LoadCloset</c> : class, index, 3
    /// couleurs u64). Clé = class_id, liste ordonnée par
    /// <c>index</c>.</summary>
    Task<SortedDictionary<byte, List<(ulong Helmet, ulong Clothes,
        ulong Cloak)>>> GetClosetsAsync(int characterId);

    /// <summary>Sélection d'un set de costume (ClosetSet 167 natif) :
    /// <c>UPDATE player_characters SET style, helmet_color,
    /// clothes_color, cloak_color</c> (SavePlayerCharacter
    /// <c>"style helmet_color clothes_color cloak_color"</c>) +
    /// <c>player_classlist.style</c> de la classe active (commande
    /// <c>classlist = %hhd %hd %d %d %hhd %hhd</c>), en transaction.</summary>
    Task SaveClosetSelectionAsync(int characterId, int classId,
        int closetIndex, ulong helmet, ulong clothes, ulong cloak);

    /// <summary>Upsert d'un slot de garde-robe (<c>TC_UpdateCloset</c>
    /// natif — DELETE puis INSERT <c>player_closet</c>, en transaction).</summary>
    Task UpsertClosetSlotAsync(int characterId, int classId,
        int index, ulong helmet, ulong clothes, ulong cloak);
}
