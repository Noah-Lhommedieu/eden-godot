using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

public interface IMailRepository
{
    Task<int> InsertMailAsync(MailRow mail);
    Task<IReadOnlyList<MailRow>> GetInboxAsync(int characterId);
    Task<MailRow?> GetMailAsync(int characterId, int mailId);
    Task MarkReadAsync(int mailId);
    Task MarkAttachmentsClaimedAsync(int mailId);
    Task DeleteMailAsync(int mailId);
    Task<int> PurgeExpiredAsync();
    Task<bool> MoveItemToMailAsync(int characterId, int bagId,
        int slot, int mailId);
}

/// <summary>Ligne table player_mail (schéma natif edenfr_db :
/// receiver_id, sender_id, sender_name, send_time, due_date, title,
/// content, item_id, gold, crystal, cod, opened, returned, authoritative).</summary>
public sealed class MailRow
{
    public int Id { get; set; }
    /// <summary>Colonne native receiver_id.</summary>
    public int RecipientId { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    /// <summary>Colonne native title.</summary>
    public string Subject { get; set; } = string.Empty;
    /// <summary>Colonne native content.</summary>
    public string Body { get; set; } = string.Empty;
    /// <summary>Colonne native gold.</summary>
    public long AttachedGold { get; set; }
    /// <summary>Colonne native item_id (item joint, un seul).</summary>
    public int AttachedItemId { get; set; }
    /// <summary>Format "itemId:qty,itemId:qty" — conservé côté C#,
    /// sérialisé dans content côté wire si besoin.</summary>
    public string AttachedItems { get; set; } = string.Empty;
    /// <summary>Colonne native cod (contre-remboursement).</summary>
    public long CodAmount { get; set; }
    /// <summary>Colonne native send_time (epoch s).</summary>
    public DateTime SentAt { get; set; }
    /// <summary>Colonne native due_date (epoch s, 0 = jamais).</summary>
    public DateTime? ExpiresAt { get; set; }
    /// <summary>Colonne native opened.</summary>
    public bool IsRead { get; set; }
    /// <summary>Colonne native returned (réutilisée : pièces jointes
    /// récupérées — pas de colonne dédiée dans le schéma natif).</summary>
    public bool AttachmentsClaimed { get; set; }
}

public sealed class MailRepository : IMailRepository
{
    private readonly NpgsqlDataSource _db;
    public MailRepository(NpgsqlDataSource db) => _db = db;

    private static int Epoch(DateTime t) =>
        (int)new DateTimeOffset(t, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>SELECT commun : alias natifs -> propriétés MailRow.</summary>
    private const string SelectCols = @"
        id AS Id, receiver_id AS RecipientId, sender_id AS SenderId,
        sender_name AS SenderName, title AS Subject, content AS Body,
        gold AS AttachedGold, item_id AS AttachedItemId, cod AS CodAmount,
        to_timestamp(send_time) AS SentAt,
        NULLIF(due_date, 0) AS DueDateRaw,
        opened AS IsReadInt, returned AS ReturnedInt";

    public async Task<int> InsertMailAsync(MailRow mail)
    {
        using var c = _db.CreateConnection();
        return await InsertMailCoreAsync(c, null, mail);
    }

    /// <summary>INSERT player_mail partagé — exécutable sur la
    /// transaction <paramref name="tx"/> quand elle est fournie
    /// (chemin enchères) ou hors transaction (envoi unitaire).
    /// Retourne l'id alloué (<see cref="MailIdAllocator"/> —
    /// séquence mail_serial partagée).</summary>
    private async Task<int> InsertMailCoreAsync(NpgsqlConnection c,
        NpgsqlTransaction? tx, MailRow mail)
    {
        const string sql = @"
            INSERT INTO player_mail (id, receiver_id, sender_id, sender_name,
                title, content, item_id, gold, crystal, cod, opened,
                returned, authoritative, send_time, due_date)
            VALUES (@Id, @RecipientId, @SenderId, @SenderName, @Subject,
                @Body, @AttachedItemId, @AttachedGold, 0, @Cod, 0, 0,
                0, @SendTime, @DueDate)
            RETURNING id";
        // La colonne native item_id ne porte qu'un item — on prend le
        // premier du format "itemId:qty,..." si AttachedItemId n'est pas posé.
        var itemId = mail.AttachedItemId;
        if (itemId == 0 && int.TryParse(
                mail.AttachedItems.Split(',')[0].Split(':')[0], out var p))
            itemId = p;
        // player_mail.id n'a ni séquence ni trigger natif —
        // séquence mail_serial, atomique cross-process
        // (MailIdAllocator, migration 012) — alloué sur la
        // connexion/tx de l'appelant, pas sur une seconde connexion.
        var id = await MailIdAllocator.NextAsync(c, tx);
        return await c.ExecuteScalarAsync<int>(sql, new
        {
            Id = id,
            mail.RecipientId, mail.SenderId, mail.SenderName, mail.Subject,
            mail.Body, AttachedItemId = itemId, mail.AttachedGold,
            Cod = (int)mail.CodAmount,
            SendTime = Epoch(mail.SentAt),
            DueDate = mail.ExpiresAt.HasValue ? Epoch(mail.ExpiresAt.Value) : 0,
        }, tx);
    }

    public async Task<IReadOnlyList<MailRow>> GetInboxAsync(int characterId)
    {
        using var c = _db.CreateConnection();
        const string sql = @"
            SELECT id, receiver_id, sender_id, sender_name, title, content,
                   item_id, gold, cod, send_time, due_date, opened, returned
            FROM player_mail
            WHERE receiver_id = @CharacterId
              AND (due_date = 0 OR due_date > @Now)
            ORDER BY send_time DESC";
        var rows = await c.QueryAsync(sql,
            new { CharacterId = characterId, Now = Epoch(DateTime.UtcNow) });
        return rows.Select(MapRow).ToList();
    }

    public async Task<MailRow?> GetMailAsync(int characterId, int mailId)
    {
        using var c = _db.CreateConnection();
        const string sql = @"
            SELECT id, receiver_id, sender_id, sender_name, title, content,
                   item_id, gold, cod, send_time, due_date, opened, returned
            FROM player_mail WHERE id = @MailId AND receiver_id = @CharacterId";
        var r = await c.QueryFirstOrDefaultAsync(sql,
            new { MailId = mailId, CharacterId = characterId });
        return r == null ? null : MapRow(r);
    }

    private static MailRow MapRow(dynamic r) => new()
    {
        Id = (int)r.id,
        RecipientId = (int)r.receiver_id,
        SenderId = (int)r.sender_id,
        SenderName = r.sender_name ?? "",
        Subject = r.title ?? "",
        Body = r.content ?? "",
        AttachedItemId = (int)r.item_id,
        AttachedItems = (int)r.item_id > 0 ? $"{(int)r.item_id}:1" : "",
        AttachedGold = (int)r.gold,
        CodAmount = (int)r.cod,
        SentAt = DateTimeOffset.FromUnixTimeSeconds((int)r.send_time).UtcDateTime,
        ExpiresAt = (int)r.due_date > 0
            ? DateTimeOffset.FromUnixTimeSeconds((int)r.due_date).UtcDateTime
            : null,
        IsRead = (int)r.opened != 0,
        AttachmentsClaimed = (int)r.returned != 0,
    };

    public async Task MarkReadAsync(int mailId)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync("UPDATE player_mail SET opened = 1 WHERE id = @MailId",
            new { MailId = mailId });
    }

    public async Task MarkAttachmentsClaimedAsync(int mailId)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "UPDATE player_mail SET returned = 1 WHERE id = @MailId",
            new { MailId = mailId });
    }

    public async Task DeleteMailAsync(int mailId)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync("DELETE FROM player_mail WHERE id = @MailId",
            new { MailId = mailId });
    }

    public async Task<int> PurgeExpiredAsync()
    {
        using var c = _db.CreateConnection();
        return await c.ExecuteAsync(
            "DELETE FROM player_mail WHERE due_date <> 0 AND due_date <= @Now",
            new { Now = Epoch(DateTime.UtcNow) });
    }

    /// <summary>Détache un item de l'inventaire vers mailitem —
    /// copie intégrale des colonnes natives (loc→mail_id). Natif :
    /// CNE_CZ_ZoneServerMailSend @0x82657f0 — l'item à
    /// (con_index,inv_loc) devient la pièce jointe du courrier.
    /// Retourne false si le slot est vide.</summary>
    public async Task<bool> MoveItemToMailAsync(int characterId,
        int bagId, int slot, int mailId)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        // INSERT mailitem (SELECT depuis la ligne inventaire) + DELETE
        // de cette ligne : une seule transaction — un crash entre les
        // deux dupliquait l'item (pièce jointe + slot inventaire).
        // Les deux buffers ping-pong inventory1/2 sont tentés, la vue
        // `inventory` n'étant pas utilisable dans le DELETE. Ordre :
        // lecture inventaire → mailitem → inventaire (déterministe).
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            var moved = await MoveItemToMailCoreAsync(c, tx,
                characterId, bagId, slot, mailId);
            if (!moved)
            {
                await tx.RollbackAsync();
                return false;
            }
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Colonnes de la ligne item — schéma natif commun
    /// inventory1/2 ↔ mailitem (seuls loc↔mail_id diffèrent).</summary>
    private const string MailItemColumns = @"
        (id, item_id, durability, maker, rank, identify, magic,
         embedded_amount, embedded_id1, embedded_id2, embedded_id3,
         embedded_id4, embedded_id5, embedded_id6, create_time,
         due_date, container_index, combo_id, extra_combo_id1,
         extra_combo_id2, extra_combo_id3, extra_combo_id4,
         extra_combo_id5, extra_combo_id6, strengthen,
         cur_maxdurability, bind, player_id, mail_id, witchcraft,
         extra_combo_id7, extra_combo_id8, extra_combo_id9,
         quality, plus_parameter, color, train_level, train_exp,
         train_slot1_type, train_slot1_level, train_slot2_type,
         train_slot2_level, train_slot3_type, train_slot3_level,
         extra_combo_id10, extra_combo_id11, gold_enchant,
         magicbox_enchant1, magicbox_enchant2, magicbox_enchant3,
         magicbox_energy, magicbox_starlevel, unbind)";

    /// <summary>Move inventaire→mailitem dans la transaction de
    /// l'appelant : INSERT mailitem (SELECT depuis la ligne
    /// inventaire) + DELETE de cette ligne. Le serial <c>id</c> est
    /// PRÉSERVÉ (miroir du SwitchContainerItem natif — l'item garde
    /// son identité à travers le déplacement ; l'ancien
    /// <c>ItemSerialAllocator.NextAsync</c> ré-allouait un serial à
    /// chaque trajet sac↔mail). Les deux buffers ping-pong
    /// inventory1/2 sont tentés — la vue `inventory` n'est pas
    /// utilisable dans le DELETE. Retourne false si le slot est
    /// vide (l'appelant rollback).</summary>
    private static async Task<bool> MoveItemToMailCoreAsync(
        NpgsqlConnection c, NpgsqlTransaction tx, int characterId,
        int bagId, int slot, int mailId)
    {
        const string selectCols = @"
            SELECT id, item_id, durability, maker, rank, identify,
                 magic, embedded_amount, embedded_id1, embedded_id2,
                 embedded_id3, embedded_id4, embedded_id5, embedded_id6,
                 create_time, due_date, container_index, combo_id,
                 extra_combo_id1, extra_combo_id2, extra_combo_id3,
                 extra_combo_id4, extra_combo_id5, extra_combo_id6,
                 strengthen, cur_maxdurability, bind, player_id, @mail,
                 witchcraft, extra_combo_id7, extra_combo_id8,
                 extra_combo_id9, quality, plus_parameter, color,
                 train_level, train_exp, train_slot1_type,
                 train_slot1_level, train_slot2_type, train_slot2_level,
                 train_slot3_type, train_slot3_level, extra_combo_id10,
                 extra_combo_id11, gold_enchant, magicbox_enchant1,
                 magicbox_enchant2, magicbox_enchant3, magicbox_energy,
                 magicbox_starlevel, unbind";
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            var copied = await c.ExecuteAsync(
                "INSERT INTO mailitem " + MailItemColumns + selectCols +
                $" FROM {table} WHERE player_id = @c" +
                " AND container_index = @bag AND loc = @slot",
                new { mail = mailId, c = characterId,
                      bag = bagId, slot }, tx);
            if (copied == 0) continue;
            await c.ExecuteAsync(
                $"DELETE FROM {table} WHERE player_id = @c " +
                "AND container_index = @bag AND loc = @slot",
                new { c = characterId, bag = bagId, slot }, tx);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Envoi de courrier joueur en UNE transaction : débit gardé
    /// (or joint + taxe — solde insuffisant → refus total), INSERT
    /// <c>player_mail</c>, puis move de la pièce jointe
    /// inventaire→mailitem. Avant, les trois écritures partaient sur
    /// des connexions distinctes (un crash laissait un mail orphelin
    /// ou un item débité sans attachement ; le débit absolu
    /// <c>SetGold</c> perdait aussi les écritures concurrentes).
    /// <paramref name="bagId"/>/<paramref name="slot"/> &lt; 0 → pas
    /// d'item joint. Retourne (mailId, or post-débit) ou null si la
    /// tx a été refusée.
    /// </summary>
    public async Task<(int MailId, long Gold)?> SendMailAsync(
        MailRow mail, int senderId, int bagId, int slot, long debit)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            // Débit gardé — solde insuffisant entre le check et la
            // tx → rollback intégral.
            long gold = 0;
            if (debit > 0)
            {
                var g = await c.ExecuteScalarAsync<long?>(
                    "UPDATE player_characters " +
                    "SET gold = LEAST(999999999, GREATEST(0, gold + @d)) " +
                    "WHERE id = @id AND gold + @d >= 0 " +
                    "RETURNING gold::bigint",
                    new { id = senderId, d = -debit }, tx);
                if (g == null)
                {
                    await tx.RollbackAsync();
                    return null;
                }
                gold = g.Value;
            }
            var mailId = await InsertMailCoreAsync(c, tx, mail);
            // Pièce jointe : le slot peut s'être vidé entre le check
            // handler et la tx → refus total plutôt que mail sans
            // item (l'attachement serait irrécupérable).
            if (bagId >= 0 && slot >= 0 &&
                !await MoveItemToMailCoreAsync(c, tx, senderId, bagId,
                    slot, mailId))
            {
                await tx.RollbackAsync();
                return null;
            }
            await tx.CommitAsync();
            return (mailId, gold);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>INSERT d'une ligne <c>mailitem</c> synthétisée dans
    /// la transaction de l'appelant — utilisée pour les pièces
    /// jointes d'enchères (achat/retour) : l'item n'existe plus en
    /// inventaire à copier, la ligne est recréée depuis les données
    /// de l'annonce (itemId + durability ; les attributs d'instance
    /// non retenus par l'annonce restent aux valeurs de création —
    /// identify=1, embedded_idN=−1). Serial neuf via
    /// <see cref="ItemSerialAllocator"/> dans la tx.</summary>
    private static async Task InsertMailAttachmentAsync(
        NpgsqlConnection c, NpgsqlTransaction tx, int ownerId,
        int mailId, int itemId, int durability, int maxDurability)
    {
        var serial = await ItemSerialAllocator.NextAsync(c, tx);
        await c.ExecuteAsync(
            "INSERT INTO mailitem " + MailItemColumns + @" VALUES
                (@id, @iid, @dur, '', 0, 1, 0, 0,
                 -1, -1, -1, -1, -1, -1,
                 @ctime, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                 0, @maxd, 0, @pid, @mail, 0, 0, 0, 0,
                 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                 0, 0, 0, 0, 0, 0, 0, 0, 0)",
            new
            {
                id = serial, iid = itemId, dur = durability,
                ctime = Epoch(DateTime.UtcNow), maxd = maxDurability,
                pid = ownerId, mail = mailId,
            }, tx);
    }

    /// <summary>
    /// Règlement complet d'un achat à l'hôtel des ventes en UNE
    /// transaction : débit de l'acheteur gardé par le solde
    /// (<c>gold &gt;= @cost</c> — re-check atomique du test hors tx),
    /// crédit du vendeur en delta clampé au cap natif 999 999 999,
    /// bascule <c>active_auctions.status='sold'</c> (gardée par
    /// <c>status='active'</c> — une annonce vendue/expirée/annulée
    /// entre-temps refuse la vente), puis INSERT du mail de livraison
    /// ET de sa ligne <c>mailitem</c> (avant, seul le mail existait :
    /// la pièce jointe était irrécupérable via MailGetItem).
    /// Ordre de verrouillage : <c>player_characters</c> en charId
    /// croissant, <c>active_auctions</c>, puis <c>player_mail</c>/
    /// <c>mailitem</c>. Retourne false — rollback intégral — si le
    /// débit est refusé ou l'annonce n'est plus active.
    /// </summary>
    public async Task<bool> SettleAuctionPurchaseAsync(int buyerId,
        long cost, int sellerId, long sellerGain, MailRow mail,
        int auctionId, int durability, int maxDurability)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            // charId croissant — ordre déterministe des verrous.
            foreach (var (id, delta, guarded) in new[]
                     {
                         (buyerId, -cost, true),
                         (sellerId, sellerGain, false),
                     }.OrderBy(x => x.Item1))
            {
                var g = await c.ExecuteScalarAsync<long?>(
                    "UPDATE player_characters " +
                    "SET gold = LEAST(999999999, GREATEST(0, gold + @d)) " +
                    "WHERE id = @id" +
                    (guarded ? " AND gold + @d >= 0" : "") +
                    " RETURNING gold::bigint",
                    new { id, d = delta }, tx);
                if (g == null)
                {
                    await tx.RollbackAsync();
                    return false;
                }
            }
            // Transition d'état de l'annonce DANS la tx — avant,
            // UpdateAuctionAsync partait sur une autre connexion
            // (BuyAsync) : un crash réglait l'or sans marquer
            // l'annonce vendue (revente possible).
            var marked = await c.ExecuteAsync(
                "UPDATE active_auctions SET status = 'sold' " +
                "WHERE id = @a AND status = 'active'",
                new { a = auctionId }, tx);
            if (marked == 0)
            {
                await tx.RollbackAsync();
                return false;
            }
            var mailId = await InsertMailCoreAsync(c, tx, mail);
            if (mail.AttachedItemId > 0)
                await InsertMailAttachmentAsync(c, tx, buyerId, mailId,
                    mail.AttachedItemId, durability, maxDurability);
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Retour d'item d'enchère (annulation/expiration) en UNE
    /// transaction : bascule <c>active_auctions.status</c>
    /// ('cancelled'/'expired', gardée par <c>status='active'</c>),
    /// INSERT du mail de retour au vendeur + ligne <c>mailitem</c>.
    /// Avant, l'annulation supprimait l'annonce puis réinsérait
    /// l'item en inventaire hors transaction — un crash ou un sac
    /// plein perdait l'item. Le mail (30 j) est la destination de
    /// retour native : il ne dépend pas de la place libre en sac.
    /// Retourne false — rollback — si l'annonce n'est plus active.
    /// </summary>
    public async Task<bool> SettleAuctionReturnAsync(int auctionId,
        string status, MailRow mail, int durability, int maxDurability)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            var marked = await c.ExecuteAsync(
                "UPDATE active_auctions SET status = @s " +
                "WHERE id = @a AND status = 'active'",
                new { s = status, a = auctionId }, tx);
            if (marked == 0)
            {
                await tx.RollbackAsync();
                return false;
            }
            var mailId = await InsertMailCoreAsync(c, tx, mail);
            await InsertMailAttachmentAsync(c, tx, mail.RecipientId,
                mailId, mail.AttachedItemId, durability, maxDurability);
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
