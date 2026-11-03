using Dapper;
using Eden.Core.Game.Inventory;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Persistance de `fast_dress` — mêmes requêtes que le natif :
/// `SELECT player_id, id, item_id, container_index, from_loc,
/// from_index, page, tar_loc FROM fast_dress WHERE player_id = %d`,
/// `INSERT INTO fast_dress (...) VALUES (...)`,
/// `UPDATE fast_dress SET id=… WHERE player_id AND page AND tar_loc`
/// (mission `CExecutionerDBO::LoadFastDress` @0x8156820,
/// `CExecutionerData::GetUpdateFastDressInfoSQLCmd` @0x832e4f0).
/// Le seed natif (72 lignes — 24 clés × 3 pages vides quand le
/// SELECT est vide) est reproduit à l'identique.
/// </summary>
public sealed class FastDressRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Injecte la source Npgsql edenfr_db.</summary>
    public FastDressRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Charge tous les records d'un personnage ; si la table
    /// est vide pour lui, INSÈRE les 72 lignes de seed natives
    /// (uid=0, item=0, con=0, loc=-1, idx=-1) puis les retourne —
    /// comportement exact de `CExecutionerDBO::LoadFastDress`.</summary>
    public async Task<List<FastDressRecord>> LoadAsync(int playerId,
        CancellationToken ct = default)
    {
        await using var db = _db.CreateConnection();
        var rows = (await db.QueryAsync<FastDressRow>(
            "SELECT id, item_id, container_index, from_loc," +
            " from_index, page, tar_loc FROM fast_dress" +
            " WHERE player_id = @P",
            new { P = playerId })).ToList();
        if (rows.Count == 0)
        {
            await SeedAsync(db, playerId);
            rows = (await db.QueryAsync<FastDressRow>(
                "SELECT id, item_id, container_index, from_loc," +
                " from_index, page, tar_loc FROM fast_dress" +
                " WHERE player_id = @P",
                new { P = playerId })).ToList();
        }
        return rows.Select(r => new FastDressRecord
        {
            Uid = r.Id, ItemId = r.ItemId,
            ContainerType = r.ContainerIndex,
            FromLoc = r.FromLoc, FromIndex = r.FromIndex,
            Page = r.Page, TarLoc = r.TarLoc,
        }).ToList();
    }

    /// <summary>INSERT des records par défaut natifs pour les
    /// <see cref="FastDressSlots.PageCount"/> pages × 24 clés —
    /// `CExecutionerDBO::LoadFastDress` : boucle page puis
    /// `GameData::FastDressEquipLoc`. Regroupé en UNE requête :
    /// generate_series (pages 0..PageCount-1) × unnest (clés
    /// tar_loc) produit les mêmes 72 lignes en un aller-retour ;
    /// ON CONFLICT = robustesse si une ligne apparaît entre le
    /// SELECT vide et ce seed (PK player_id+page+tar_loc).</summary>
    private static async Task SeedAsync(NpgsqlConnection db, int playerId)
    {
        const string sql =
            "INSERT INTO fast_dress (player_id, id, item_id," +
            " container_index, from_loc, from_index, page, tar_loc)" +
            " SELECT @P, 0, 0, 0, -1, -1, p.page, k.tar_loc" +
            " FROM generate_series(0, @PageCount - 1) AS p(page)" +
            " CROSS JOIN unnest(@TarLocs) AS k(tar_loc)" +
            " ON CONFLICT (player_id, page, tar_loc) DO NOTHING";
        // int[] explicite : Npgsql lie le tableau comme array PG pour
        // unnest (EquipLocs est ushort[] — les colonnes sont integer).
        var tarLocs = FastDressSlots.EquipLocs
            .Select(k => (int)k).ToArray();
        await db.ExecuteAsync(sql, new
        {
            P = playerId,
            PageCount = FastDressSlots.PageCount,
            TarLocs = tarLocs,
        });
    }

    /// <summary>`CItemDBO::UpdateFastDressData` — UPDATE complet du
    /// record (Save) ; INSERT si la ligne manque (robustesse — la PK
    /// native garantit l'unicité).</summary>
    public async Task UpdateRecordAsync(int playerId,
        FastDressRecord r)
    {
        const string upd =
            "UPDATE fast_dress SET id = @U, item_id = @I," +
            " container_index = @C, from_loc = @L, from_index = @X" +
            " WHERE player_id = @P AND page = @Pg AND tar_loc = @T";
        await using var db = _db.CreateConnection();
        var n = await db.ExecuteAsync(upd, new
        {
            U = r.Uid, I = r.ItemId, C = r.ContainerType,
            L = r.FromLoc, X = r.FromIndex,
            P = playerId, Pg = r.Page, T = r.TarLoc,
        });
        if (n == 0)
            await db.ExecuteAsync(
                "INSERT INTO fast_dress (player_id, id, item_id," +
                " container_index, from_loc, from_index, page," +
                " tar_loc) VALUES (@P,@U,@I,@C,@L,@X,@Pg,@T)", new
                {
                    U = r.Uid, I = r.ItemId, C = r.ContainerType,
                    L = r.FromLoc, X = r.FromIndex,
                    P = playerId, Pg = r.Page, T = r.TarLoc,
                });
    }

    /// <summary>`CItemDBO::UpdateFastDressInfo` — met à jour la
    /// position source d'un record après déplacement de l'item
    /// (hook `CCharacter::ChangeFastDressInfo`) : SET
    /// container_index/from_loc/from_index WHERE player+page+tar_loc.</summary>
    public async Task UpdatePositionAsync(int playerId, int page,
        int tarLoc, int containerType, int fromLoc, int fromIndex)
    {
        const string sql =
            "UPDATE fast_dress SET container_index = @C," +
            " from_loc = @L, from_index = @X" +
            " WHERE player_id = @P AND page = @Pg AND tar_loc = @T";
        await using var db = _db.CreateConnection();
        await db.ExecuteAsync(sql, new
        {
            C = containerType, L = fromLoc, X = fromIndex,
            P = playerId, Pg = page, T = tarLoc,
        });
    }

    /// <summary>Ligne brute `fast_dress` (Dapper
    /// MatchNamesWithUnderscores).</summary>
    private sealed class FastDressRow
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public int ContainerIndex { get; set; }
        public int FromLoc { get; set; }
        public int FromIndex { get; set; }
        public int Page { get; set; }
        public int TarLoc { get; set; }
    }
}
