using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Persistance des familiers de combat (elf pets) — table native
/// <c>elf</c> de edenfr_db (miroir des lignes "Insert Elf" /
/// "Update Elf" du natif — ElfManager.cc @0x807xxxx) :
///
/// <code>
/// elf_id | player_id | template_id | status | given_name | level |
/// exp | hp | grow_attack | grow_hit | grow_critical |
/// grow_attackspeed | loc | mix_star | battle_spell_1..5 |
/// integrate_spell_1..5
/// </code>
///
/// - <c>template_id</c> = id de l'item "œuf" (S_Partner col 0, ex.
///   23993 Combat Bunny) — pas l'elf_id ;
/// - <c>loc</c> = slot 0-9 dans le tableau d'elfs du perso
///   (char+0x8ac natif — 10 slots max) ;
/// - <c>status</c> = 0 inactif, 2 invoqué/combat (SetElfStatus natif),
///   7/8/9 = états de loterie ;
/// - la table n'a <b>pas de séquence native</c> — <c>NextElfIdAsync</c>
///   alloue via la séquence <c>elf_serial</c> (migration 012,
///   atomique cross-process).
/// </summary>
public sealed class ElfRepository
{
    private readonly NpgsqlDataSource _db;

    public ElfRepository(NpgsqlDataSource db)
        => _db = db;

    private NpgsqlConnection Open() => _db.CreateConnection();

    /// <summary>Ligne de la table <c>elf</c> native.</summary>
    public sealed class ElfRow
    {
        public int ElfId { get; set; }
        public int PlayerId { get; set; }
        /// <summary>Id de l'item partner (S_Partner clé).</summary>
        public int TemplateId { get; set; }
        /// <summary>0 = inactif, 2 = invoqué (SetElfStatus natif).</summary>
        public short Status { get; set; }
        public string GivenName { get; set; } = string.Empty;
        public int Level { get; set; } = 1;
        public int Exp { get; set; }
        /// <summary>HP courant (CKFElf+0x1c).</summary>
        public int Hp { get; set; }
        /// <summary>Index de croissance attack (0-9 → S_PartnerGrow
        /// cols 1-10).</summary>
        public short GrowAttack { get; set; }
        /// <summary>Index de croissance hit (cols 11-20).</summary>
        public short GrowHit { get; set; }
        /// <summary>Index de croissance critical (cols 21-30).</summary>
        public short GrowCritical { get; set; }
        /// <summary>Index de croissance attack speed (cols 31-40).</summary>
        public short GrowAttackSpeed { get; set; }
        /// <summary>Slot 0-9 du tableau d'elfs du perso.</summary>
        public short Loc { get; set; }
        public short MixStar { get; set; }
        public int BattleSpell1 { get; set; }
        public int BattleSpell2 { get; set; }
        public int BattleSpell3 { get; set; }
        public int BattleSpell4 { get; set; }
        public int BattleSpell5 { get; set; }
        public int IntegrateSpell1 { get; set; }
        public int IntegrateSpell2 { get; set; }
        public int IntegrateSpell3 { get; set; }
        public int IntegrateSpell4 { get; set; }
        public int IntegrateSpell5 { get; set; }

        /// <summary>Sorts de bataille en tableau (5 slots).</summary>
        public int[] BattleSpells =>
            new[] { BattleSpell1, BattleSpell2, BattleSpell3,
                BattleSpell4, BattleSpell5 };

        /// <summary>Sorts intégrés en tableau (5 slots).</summary>
        public int[] IntegrateSpells =>
            new[] { IntegrateSpell1, IntegrateSpell2, IntegrateSpell3,
                IntegrateSpell4, IntegrateSpell5 };
    }

    private const string Columns =
        "elf_id, player_id, template_id, status, given_name, level, " +
        "exp, hp, grow_attack, grow_hit, grow_critical, " +
        "grow_attackspeed, loc, mix_star, " +
        "battle_spell_1, battle_spell_2, battle_spell_3, " +
        "battle_spell_4, battle_spell_5, integrate_spell_1, " +
        "integrate_spell_2, integrate_spell_3, integrate_spell_4, " +
        "integrate_spell_5";

    /// <summary>Charge tous les elfs d'un personnage, triés par slot
    /// (ordre du tableau natif char+0x8ac — le client adresse les
    /// elfs par loc).</summary>
    public async Task<IReadOnlyList<ElfRow>> GetElfsAsync(int playerId)
    {
        using var db = Open();
        return (await db.QueryAsync<ElfRow>(
            $"SELECT {Columns} FROM elf WHERE player_id=@playerId " +
            "ORDER BY loc", new { playerId })).AsList();
    }

    /// <summary>Prochain elf_id — séquence <c>elf_serial</c>
    /// (migration 012) : nextval est atomique et cross-process,
    /// contrairement au MAX+1 sans verrou précédent (deux appels
    /// concurrents pouvaient lire le même max).</summary>
    public async Task<int> NextElfIdAsync()
    {
        using var db = Open();
        // nextval rend bigint — cast borné : elf.elf_id est int4.
        return checked((int)await db.ExecuteScalarAsync<long>(
            "SELECT nextval('elf_serial')"));
    }

    /// <summary>INSERT complet d'un nouvel elf (ligne "Insert Elf"
    /// du natif — tous les champs persistés à la création).</summary>
    public async Task InsertElfAsync(ElfRow e)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "INSERT INTO elf (" + Columns + ") VALUES (" +
            "@ElfId, @PlayerId, @TemplateId, @Status, @GivenName, " +
            "@Level, @Exp, @Hp, @GrowAttack, @GrowHit, @GrowCritical, " +
            "@GrowAttackSpeed, @Loc, @MixStar, " +
            "@BattleSpell1, @BattleSpell2, @BattleSpell3, " +
            "@BattleSpell4, @BattleSpell5, @IntegrateSpell1, " +
            "@IntegrateSpell2, @IntegrateSpell3, @IntegrateSpell4, " +
            "@IntegrateSpell5)", e);
    }

    /// <summary>Mise à jour du statut (SetElfStatus natif — 0 renvoi,
    /// 2 invocation).</summary>
    public async Task UpdateStatusAsync(int elfId, short status)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE elf SET status=@status WHERE elf_id=@elfId",
            new { elfId, status });
    }

    /// <summary>HP courant persisté (renvoi du pet — "Update Elf" du
    /// natif conserve hp au rappel).</summary>
    public async Task UpdateHpAsync(int elfId, int hp)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE elf SET hp=@hp WHERE elf_id=@elfId",
            new { elfId, hp });
    }

    /// <summary>Renommage (ElfChangeName natif — given_name).</summary>
    public async Task UpdateNameAsync(int elfId, string name)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE elf SET given_name=@name WHERE elf_id=@elfId",
            new { elfId, name });
    }

    /// <summary>Progression (level/exp à la montée de niveau).</summary>
    public async Task UpdateLevelExpAsync(int elfId, int level, int exp)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE elf SET level=@level, exp=@exp WHERE elf_id=@elfId",
            new { elfId, level, exp });
    }

    /// <summary>Persistance complète d'une fusion — DBA natif
    /// « Update Elf Mix » (DoElfUpgrade @0x807a0f0) : hp, 4 grow,
    /// mix_star, 5 sorts de bataille, 5 sorts intégrés. Le natif
    /// remet level=1/exp=0 — portés par l'appelant (la ligne est
    /// déjà à jour côté colonnes non listées).</summary>
    public async Task UpdateElfMixAsync(ElfRow e)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE elf SET level=@Level, exp=@Exp, hp=@Hp, " +
            "grow_attack=@GrowAttack, grow_hit=@GrowHit, " +
            "grow_critical=@GrowCritical, " +
            "grow_attackspeed=@GrowAttackSpeed, mix_star=@MixStar, " +
            "battle_spell_1=@BattleSpell1, battle_spell_2=@BattleSpell2, " +
            "battle_spell_3=@BattleSpell3, battle_spell_4=@BattleSpell4, " +
            "battle_spell_5=@BattleSpell5, " +
            "integrate_spell_1=@IntegrateSpell1, " +
            "integrate_spell_2=@IntegrateSpell2, " +
            "integrate_spell_3=@IntegrateSpell3, " +
            "integrate_spell_4=@IntegrateSpell4, " +
            "integrate_spell_5=@IntegrateSpell5 " +
            "WHERE elf_id=@ElfId", e);
    }

    /// <summary>Persistance des 10 slots de sorts — DBA natif
    /// « Update Elf Spell » émis par <c>CKFElf::LearnSpell</c>
    /// @0x8455980 / <c>LearnSpellFromGroup</c> @0x8455ec0 (livres de
    /// sorts pet — enchant 6035-6038).</summary>
    /// <param name="battle">Les 5 ids de sorts de bataille
    /// (spell ids).</param>
    /// <param name="integrate">Les 5 ids de sorts intégrés (enchant
    /// ids — cf. map enchant+0x94).</param>
    public async Task UpdateElfSpellsAsync(int elfId,
        IReadOnlyList<int> battle, IReadOnlyList<int> integrate)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE elf SET battle_spell_1=@B1, battle_spell_2=@B2, " +
            "battle_spell_3=@B3, battle_spell_4=@B4, " +
            "battle_spell_5=@B5, integrate_spell_1=@I1, " +
            "integrate_spell_2=@I2, integrate_spell_3=@I3, " +
            "integrate_spell_4=@I4, integrate_spell_5=@I5 " +
            "WHERE elf_id=@ElfId",
            new
            {
                B1 = battle[0], B2 = battle[1], B3 = battle[2],
                B4 = battle[3], B5 = battle[4],
                I1 = integrate[0], I2 = integrate[1],
                I3 = integrate[2], I4 = integrate[3],
                I5 = integrate[4], ElfId = elfId
            });
    }

    /// <summary>Suppression d'un elf (DBA natif « Delete ElfLeave »
    /// de ReturnElf — elf consommé par la fusion ou libéré).</summary>
    public async Task DeleteElfAsync(int elfId)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "DELETE FROM elf WHERE elf_id=@elfId", new { elfId });
    }
}
