using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

public class MailboxClient
{
    private readonly EmailTextExtractor emailTextExtractor;

    public MailboxClient(EmailTextExtractor emailTextExtractor)
    {
        this.emailTextExtractor = emailTextExtractor;
    }

    public async Task<List<MailboxEmail>> FetchUnreadMessagesAsync(MailboxSettings settings)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.UseSsl);
        await client.AuthenticateAsync(settings.User, settings.Password);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly);

        var unreadIds = await inbox.SearchAsync(SearchQuery.NotSeen);
        var messages = new List<MailboxEmail>();

        foreach (var uid in unreadIds.OrderByDescending(id => id.Id))
        {
            if (messages.Count >= settings.MaxMessages)
            {
                break;
            }

            var message = await inbox.GetMessageAsync(uid);
            var identifier = CreateMessageIdentifier(message, uid);

            messages.Add(new MailboxEmail(
                identifier,
                uid.Id,
                message.From.ToString(),
                message.Subject ?? "",
                emailTextExtractor.ExtractText(message),
                message
            ));
        }

        await client.DisconnectAsync(true);
        return messages;
    }

    public async Task MarkSeenAsync(MailboxSettings settings, IEnumerable<uint> uids)
    {
        var uniqueIds = uids
            .Select(uid => new UniqueId(uid))
            .ToList();

        if (uniqueIds.Count == 0)
        {
            return;
        }

        using var client = new ImapClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.UseSsl);
        await client.AuthenticateAsync(settings.User, settings.Password);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite);
        await inbox.AddFlagsAsync(uniqueIds, MessageFlags.Seen, true);
        await client.DisconnectAsync(true);
    }

    private static string CreateMessageIdentifier(MimeMessage message, UniqueId uid)
    {
        return string.IsNullOrWhiteSpace(message.MessageId)
            ? $"inbox-uid:{uid.Id}"
            : $"message-id:{message.MessageId}";
    }
}

public record MailboxEmail(
    string MessageIdentifier,
    uint Uid,
    string From,
    string Subject,
    string Text,
    MimeMessage OriginalMessage
);
