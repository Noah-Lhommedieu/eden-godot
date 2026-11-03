using Dapper;
using Npgsql;
using Eden.Core.Game.Classes;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Dépôt `custom_class_data` + `player_characters.current_custom_data_index`
/// — équivalent des `CExecutionerDBO::LoadCustomClassData` /
/// `SaveCustomClassData` natifs (mission @~0x08153axx / appelé par
/// `CNE_ZMis_MissionServerPlayerCCDataSave::Execute` @0x08141710).
/// La colonne `index` est un mot réservé SQL — toujours citée.
/// </summary>
public sealed class CustomClassRepository
{
    private readonly NpgsqlDataSource _db;

    public CustomClassRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Charge les 3 cartes du perso (SELECT natif
    /// `index, equip_type, weapon_type, spell_1..8`) et seed les lignes
    /// manquantes comme `LoadCustomClassData` : INSERT
    /// `(equip_type=1, weapon_type=0, sorts=0)` jusqu'à 3 cartes.
    /// Retourne les cartes ordonnées par index (trous comblés par le
    /// défaut natif).</summary>
    public async Task<List<CustomClassSlot>> LoadSlotsAsync(int charId)
    {
        using var c = _db.CreateConnection();
        var rows = (await c.QueryAsync<Row>(
            @"SELECT ""index"" AS Idx, equip_type AS EquipType,
                     weapon_type AS WeaponType,
                     spell_1, spell_2, spell_3, spell_4,
                     spell_5, spell_6, spell_7, spell_8
              FROM custom_class_data WHERE player_id = @CharId
              ORDER BY ""index""",
            new { CharId = charId })).ToList();

        var slots = Enumerable.Range(0, CustomClass.SlotCount)
            .Select(_ => CustomClassSlot.Default()).ToList();
        foreach (var r in rows)
        {
            if (r.Idx is < 0 || r.Idx >= CustomClass.SlotCount) continue;
            var slot = slots[r.Idx];
            slot.EquipType = (byte)r.EquipType;
            slot.WeaponType = (byte)r.WeaponType;
            slot.Spells.Clear();
            foreach (var s in r.Spells())
                if (s != 0) slot.Spells.Add(s);
        }

        // Seed natif : INSERT des lignes absentes (equip_type=1,
        // weapon_type=0, sorts=0) jusqu'à 3 cartes — regroupé en une
        // seule requête unnest ; ON CONFLICT sur la PK
        // (player_id, "index") par robustesse (double appel concurrent).
        var present = rows.Select(r => r.Idx).ToHashSet();
        var missing = Enumerable.Range(0, CustomClass.SlotCount)
            .Where(i => !present.Contains(i)).ToArray();
        if (missing.Length > 0)
            await c.ExecuteAsync(
                @"INSERT INTO custom_class_data
                  (player_id, ""index"", equip_type, weapon_type,
                   spell_1, spell_2, spell_3, spell_4,
                   spell_5, spell_6, spell_7, spell_8)
                  SELECT @CharId, m.idx, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0
                  FROM unnest(@Idxs) AS m(idx)
                  ON CONFLICT (player_id, ""index"") DO NOTHING",
                new { CharId = charId, Idxs = missing });
        return slots;
    }

    /// <summary>`player_characters.current_custom_data_index` —
    /// exec+0x88c natif (le natif le borne à [0,3) au load : un index
    /// ≥ nb cartes retombe à 0).</summary>
    public async Task<int> GetActiveIndexAsync(int charId)
    {
        using var c = _db.CreateConnection();
        var idx = await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(current_custom_data_index, 0)" +
            " FROM player_characters WHERE id = @CharId",
            new { CharId = charId });
        return idx is >= 0 && idx < CustomClass.SlotCount ? idx : 0;
    }

    /// <summary>UPDATE de la carte sauvegardée —
    /// `SaveCustomClassData` natif (`UPDATE custom_class_data SET
    /// equip_type, weapon_type, spell_1..8 WHERE player_id AND index`).</summary>
    public async Task SaveSlotAsync(int charId, int slot,
        CustomClassSlot data)
    {
        var spells = new int[CustomClass.MaxSpells];
        for (var i = 0; i < data.Spells.Count && i < spells.Length; i++)
            spells[i] = data.Spells[i];
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            @"UPDATE custom_class_data
              SET equip_type = @Equip, weapon_type = @Weapon,
                  spell_1 = @S1, spell_2 = @S2, spell_3 = @S3,
                  spell_4 = @S4, spell_5 = @S5, spell_6 = @S6,
                  spell_7 = @S7, spell_8 = @S8
              WHERE player_id = @CharId AND ""index"" = @Slot",
            new
            {
                CharId = charId, Slot = slot,
                Equip = (int)data.EquipType,
                Weapon = (int)data.WeaponType,
                S1 = spells[0], S2 = spells[1], S3 = spells[2],
                S4 = spells[3], S5 = spells[4], S6 = spells[5],
                S7 = spells[6], S8 = spells[7],
            });
    }

    /// <summary>`UPDATE player_characters SET
    /// current_custom_data_index` — champ exec+0x88c du gros UPDATE
    /// `GetUpdateSQLCmd` natif (posé à chaque CCDataSave mission
    /// @0x08141710 : `exec+0x88c = slot`, dirty flag).</summary>
    public async Task SetActiveIndexAsync(int charId, int slot)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "UPDATE player_characters SET" +
            " current_custom_data_index = @Slot WHERE id = @CharId",
            new { CharId = charId, Slot = slot });
    }

    private sealed class Row
    {
        public int Idx { get; set; }
        public int EquipType { get; set; }
        public int WeaponType { get; set; }
        public int Spell1 { get; set; }
        public int Spell2 { get; set; }
        public int Spell3 { get; set; }
        public int Spell4 { get; set; }
        public int Spell5 { get; set; }
        public int Spell6 { get; set; }
        public int Spell7 { get; set; }
        public int Spell8 { get; set; }
        public IEnumerable<int> Spells()
        {
            yield return Spell1; yield return Spell2;
            yield return Spell3; yield return Spell4;
            yield return Spell5; yield return Spell6;
            yield return Spell7; yield return Spell8;
        }
    }
}
