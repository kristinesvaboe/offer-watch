public class MailboxProcessor
{
    private readonly MailboxClient mailboxClient;
    private readonly MailboxForwarder mailboxForwarder;
    private readonly OfferProcessor offerProcessor;

    public MailboxProcessor(
        MailboxClient mailboxClient,
        MailboxForwarder mailboxForwarder,
        OfferProcessor offerProcessor
    )
    {
        this.mailboxClient = mailboxClient;
        this.mailboxForwarder = mailboxForwarder;
        this.offerProcessor = offerProcessor;
    }

    public async Task<MailboxProcessingResult> ProcessAsync(
        MailboxSettings mailboxSettings,
        SmtpSettings smtpSettings,
        string forwardingRecipient,
        Watchlist watchlist,
        Action<string>? debugExtractedText
    )
    {
        List<MailboxEmail> emails;

        try
        {
            emails = await mailboxClient.FetchUnreadMessagesAsync(mailboxSettings);
        }
        catch (Exception ex)
        {
            var error = CreateSafeMailboxError(ex);
            return new MailboxProcessingResult(
                new MailboxOutput([], error),
                [],
                new MailboxRunSummary(0, 0, 0, 1, 0),
                []
            );
        }

        var mailboxResults = new List<MailboxMessageOutput>();
        var seenUids = new List<uint>();
        var messageLogs = new List<MailboxMessageRunLog>();

        foreach (var email in emails)
        {
            debugExtractedText?.Invoke(email.Text);

            var matches = await offerProcessor.ProcessAsync(email.Text, watchlist);
            var aiFailed = HasAiUnavailableMatch(matches);
            var relevant = HasAiRelevantMatch(matches);
            var forwarded = false;
            var shouldMarkRead = false;
            string? error = null;

            mailboxResults.Add(new MailboxMessageOutput(
                email.MessageIdentifier,
                email.Uid,
                email.From,
                email.Subject,
                relevant,
                matches
            ));

            if (matches.Count == 0)
            {
                shouldMarkRead = true;
            }
            else if (aiFailed)
            {
                error = "AI evaluation unavailable";
            }
            else if (relevant)
            {
                try
                {
                    await mailboxForwarder.ForwardRelevantAsync(
                        smtpSettings,
                        forwardingRecipient,
                        email,
                        matches
                    );
                    forwarded = true;
                    shouldMarkRead = true;
                }
                catch (Exception ex)
                {
                    // Scheduled runs should keep processing other messages.
                    // This message stays unread because it was not fully handled.
                    error = $"Forwarding failed: {CreateSafeMailboxError(ex)}";
                }
            }
            else
            {
                shouldMarkRead = true;
            }

            if (shouldMarkRead)
            {
                seenUids.Add(email.Uid);
            }

            messageLogs.Add(new MailboxMessageRunLog(
                email.Uid,
                email.Subject,
                matches.Count > 0,
                relevant,
                forwarded,
                false,
                !string.IsNullOrWhiteSpace(error),
                error
            ));
        }

        var markReadError = await MarkSeenAsync(mailboxSettings, seenUids);
        if (markReadError is not null)
        {
            var seenUidSet = seenUids.ToHashSet();
            for (var i = 0; i < messageLogs.Count; i++)
            {
                var log = messageLogs[i];
                if (seenUidSet.Contains(log.Uid))
                {
                    messageLogs[i] = log with
                    {
                        Failed = true,
                        Error = $"Mark read failed: {markReadError}"
                    };
                }
            }
        }
        else
        {
            var seenUidSet = seenUids.ToHashSet();
            for (var i = 0; i < messageLogs.Count; i++)
            {
                var log = messageLogs[i];
                if (seenUidSet.Contains(log.Uid))
                {
                    messageLogs[i] = log with { MarkedRead = true };
                }
            }
        }

        return new MailboxProcessingResult(
            new MailboxOutput(mailboxResults),
            seenUids,
            CreateSummary(messageLogs),
            messageLogs
        );
    }

    public static void WriteRunLog(MailboxProcessingResult result)
    {
        Console.WriteLine($"Unread messages found: {result.Summary.TotalUnread}");
        Console.WriteLine();

        foreach (var message in result.MessageLogs)
        {
            Console.WriteLine($"Subject: {message.Subject}");
            Console.WriteLine($"Rule candidates: {FormatBool(message.HadRuleCandidates)}");
            Console.WriteLine($"AI relevant: {FormatBool(message.AiRelevant)}");
            Console.WriteLine($"Forwarded: {FormatBool(message.Forwarded)}");
            Console.WriteLine($"Marked read: {FormatBool(message.MarkedRead)}");

            if (!string.IsNullOrWhiteSpace(message.Error))
            {
                Console.WriteLine($"Error: {message.Error}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("Summary:");
        Console.WriteLine($"Total unread messages found: {result.Summary.TotalUnread}");
        Console.WriteLine($"Forwarded: {result.Summary.Forwarded}");
        Console.WriteLine($"Ignored/not relevant: {result.Summary.IgnoredNotRelevant}");
        Console.WriteLine($"Failed: {result.Summary.Failed}");
        Console.WriteLine($"Left unread: {result.Summary.LeftUnread}");
    }

    private async Task<string?> MarkSeenAsync(MailboxSettings settings, IReadOnlyList<uint> seenUids)
    {
        try
        {
            await mailboxClient.MarkSeenAsync(settings, seenUids);
            return null;
        }
        catch (Exception ex)
        {
            return CreateSafeMailboxError(ex);
        }
    }

    private static MailboxRunSummary CreateSummary(IReadOnlyList<MailboxMessageRunLog> messageLogs)
    {
        return new MailboxRunSummary(
            messageLogs.Count,
            messageLogs.Count(log => log.Forwarded),
            messageLogs.Count(log => !log.Forwarded && log.MarkedRead),
            messageLogs.Count(log => log.Failed),
            messageLogs.Count(log => !log.MarkedRead)
        );
    }

    private static string FormatBool(bool value)
    {
        return value ? "yes" : "no";
    }

    private static bool HasAiRelevantMatch(List<MatchResult> matches)
    {
        return matches.Any(match => match.AiAvailable == true && match.AiRelevant == true);
    }

    private static bool HasAiUnavailableMatch(List<MatchResult> matches)
    {
        return matches.Any(match => match.AiAvailable == false);
    }

    private static string CreateSafeMailboxError(Exception ex)
    {
        if (ex is System.Net.Sockets.SocketException || ex is HttpRequestException)
        {
            return "network_error";
        }

        if (ex is TimeoutException || ex is TaskCanceledException)
        {
            return "timeout";
        }

        if (string.IsNullOrWhiteSpace(ex.Message))
        {
            return "mailbox_error";
        }

        return ex.Message.Length <= 120
            ? ex.Message
            : ex.Message[..120];
    }
}

public record MailboxProcessingResult(
    MailboxOutput Output,
    IReadOnlyList<uint> SeenUids,
    MailboxRunSummary Summary,
    IReadOnlyList<MailboxMessageRunLog> MessageLogs
);

public record MailboxRunSummary(
    int TotalUnread,
    int Forwarded,
    int IgnoredNotRelevant,
    int Failed,
    int LeftUnread
);

public record MailboxMessageRunLog(
    uint Uid,
    string Subject,
    bool HadRuleCandidates,
    bool AiRelevant,
    bool Forwarded,
    bool MarkedRead,
    bool Failed,
    string? Error
);
