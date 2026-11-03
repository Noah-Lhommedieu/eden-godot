namespace Eden.Core.Game.Services;

/// <summary>
/// Service de mail — correspond a CMailShop + CNE_ZMis_MissionServerMail*.
/// Envoi de mails avec pieces jointes (items, gold), COD, expiration.
/// </summary>
public sealed class MailService
{
    public sealed class Mail
    {
        public int Id { get; set; }
        public int SenderId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public int RecipientId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public long AttachedGold { get; set; }
        public List<int> AttachedItemIds { get; } = new();
        public long CodAmount { get; set; }     // Cash On Delivery
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public bool IsRead { get; set; }
        public bool AttachmentsClaimed { get; set; }
    }

    public const int MaxAttachments = 5;
    public const int MailTtlDays = 30;

    private readonly Dictionary<int, Mail> _mails = new();
    private int _nextMailId = 1;

    /// <summary>Envoie un mail avec pieces jointes.</summary>
    public Mail Send(int senderId, string senderName, int recipientId,
        string subject, string body, long gold = 0,
        IEnumerable<int>? itemIds = null, long cod = 0)
    {
        var mail = new Mail
        {
            Id = _nextMailId++,
            SenderId = senderId,
            SenderName = senderName,
            RecipientId = recipientId,
            Subject = subject,
            Body = body,
            AttachedGold = gold,
            CodAmount = cod,
            ExpiresAt = DateTime.UtcNow.AddDays(MailTtlDays),
        };
        if (itemIds != null)
            mail.AttachedItemIds.AddRange(itemIds.Take(MaxAttachments));
        _mails[mail.Id] = mail;
        return mail;
    }

    /// <summary>Boite de reception d'un joueur.</summary>
    public IReadOnlyList<Mail> GetInbox(int characterId)
        => _mails.Values
            .Where(m => m.RecipientId == characterId && m.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(m => m.SentAt)
            .ToList();

    /// <summary>Lit un mail.</summary>
    public bool MarkRead(int characterId, int mailId)
    {
        var m = GetMail(characterId, mailId);
        if (m == null) return false;
        m.IsRead = true;
        return true;
    }

    /// <summary>
    /// Reclame les pieces jointes. Si COD, le paiement est retourne.
    /// </summary>
    public (long Gold, IReadOnlyList<int> Items, long CodPaid)? ClaimAttachments(
        int characterId, int mailId)
    {
        var m = GetMail(characterId, mailId);
        if (m == null || m.AttachmentsClaimed) return null;
        m.AttachmentsClaimed = true;
        return (m.AttachedGold, m.AttachedItemIds.ToList(), m.CodAmount);
    }

    /// <summary>Supprime un mail (ou retourne les pieces jointes a l'expediteur).</summary>
    public bool Delete(int characterId, int mailId)
    {
        var m = GetMail(characterId, mailId);
        if (m == null) return false;
        // Si pieces jointes non reclamees → retour a l'expediteur
        if (!m.AttachmentsClaimed &&
            (m.AttachedGold > 0 || m.AttachedItemIds.Count > 0))
        {
            Send(0, "System", m.SenderId, "Returned mail",
                m.Subject, m.AttachedGold, m.AttachedItemIds);
        }
        _mails.Remove(mailId);
        return true;
    }

    /// <summary>Purge les mails expires.</summary>
    public int PurgeExpired()
    {
        var expired = _mails.Values
            .Where(m => m.ExpiresAt <= DateTime.UtcNow).ToList();
        foreach (var m in expired)
        {
            if (!m.AttachmentsClaimed)
                Send(0, "System", m.SenderId, "Returned mail",
                    m.Subject, m.AttachedGold, m.AttachedItemIds);
            _mails.Remove(m.Id);
        }
        return expired.Count;
    }

    private Mail? GetMail(int characterId, int mailId)
        => _mails.TryGetValue(mailId, out var m) && m.RecipientId == characterId
            ? m : null;
}
