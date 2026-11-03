using System.Collections.Concurrent;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Mission.Handlers;

public record MailMessageDto(
    int MailId,
    int SenderId,
    string SenderName,
    int ReceiverId,
    string Title,
    string Content,
    int AttachedGold,
    DateTime SentAt
);

/// <summary>
/// Gestionnaire des requêtes pour MissionServer (système de guilde, boîte aux lettres, diffusion d'événements).
/// Reversé depuis lapis::CMissionServer.
/// </summary>
public sealed class MissionPacketHandlers
{
    private readonly ConcurrentDictionary<int, List<MailMessageDto>> _mailboxes = new();
    private readonly ConcurrentDictionary<int, List<NetSession>> _guildSubscribers = new();
    private int _mailCounter = 0;

    public Task<int> SendMailAsync(
        int senderId,
        string senderName,
        int receiverId,
        string title,
        string content,
        int gold = 0)
    {
        var mailId = Interlocked.Increment(ref _mailCounter);
        var mail = new MailMessageDto(mailId, senderId, senderName, receiverId, title, content, gold, DateTime.UtcNow);

        var list = _mailboxes.GetOrAdd(receiverId, _ => new List<MailMessageDto>());
        lock (list)
        {
            list.Add(mail);
        }

        EdenLog.Write($"[Mission] Courrier #{mailId} envoyé de '{senderName}' vers CharacterId={receiverId} ('{title}')");
        return Task.FromResult(mailId);
    }

    public Task<IReadOnlyList<MailMessageDto>> GetInboxAsync(int receiverId)
    {
        if (_mailboxes.TryGetValue(receiverId, out var list))
        {
            lock (list)
            {
                return Task.FromResult<IReadOnlyList<MailMessageDto>>(list.ToList());
            }
        }
        return Task.FromResult<IReadOnlyList<MailMessageDto>>(Array.Empty<MailMessageDto>());
    }

    public void SubscribeGuildChannel(int guildId, NetSession session)
    {
        var list = _guildSubscribers.GetOrAdd(guildId, _ => new List<NetSession>());
        lock (list)
        {
            if (!list.Contains(session))
            {
                list.Add(session);
            }
        }
    }

    public async Task BroadcastGuildMessageAsync(int guildId, string senderName, string message, CancellationToken cancellationToken = default)
    {
        if (_guildSubscribers.TryGetValue(guildId, out var list))
        {
            List<NetSession> targets;
            lock (list)
            {
                targets = list.ToList();
            }

            var chatMsg = new ChatMessage
            {
                ChannelType = ChatChannelType.Guild,
                SenderName = senderName,
                Message = message
            };

            foreach (var session in targets)
            {
                try
                {
                    await session.SendAsync(chatMsg, cancellationToken);
                }
                catch
                {
                    // Ignore disconnected
                }
            }
        }
    }
}
