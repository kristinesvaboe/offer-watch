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
            return new MailboxProcessingResult(new MailboxOutput([], CreateSafeMailboxError(ex)), []);
        }

        var mailboxResults = new List<MailboxMessageOutput>();
        var seenUids = new List<uint>();
        var relevantEmails = new List<(MailboxEmail Email, List<MatchResult> Matches)>();

        foreach (var email in emails)
        {
            debugExtractedText?.Invoke(email.Text);

            var matches = await offerProcessor.ProcessAsync(email.Text, watchlist);
            var aiFailed = HasAiUnavailableMatch(matches);
            var relevant = HasAiRelevantMatch(matches);

            mailboxResults.Add(new MailboxMessageOutput(
                email.MessageIdentifier,
                email.Uid,
                email.From,
                email.Subject,
                relevant,
                matches
            ));

            if (matches.Count == 0 || !aiFailed)
            {
                seenUids.Add(email.Uid);
            }

            if (relevant && !aiFailed)
            {
                relevantEmails.Add((email, matches));
            }
        }

        try
        {
            foreach (var relevantEmail in relevantEmails)
            {
                await mailboxForwarder.ForwardRelevantAsync(
                    smtpSettings,
                    forwardingRecipient,
                    relevantEmail.Email,
                    relevantEmail.Matches
                );
            }
        }
        catch (Exception ex)
        {
            return new MailboxProcessingResult(
                new MailboxOutput(mailboxResults, CreateSafeMailboxError(ex)),
                []
            );
        }

        return new MailboxProcessingResult(new MailboxOutput(mailboxResults), seenUids);
    }

    public async Task MarkProcessedMessagesSeenAsync(
        MailboxSettings settings,
        MailboxProcessingResult result
    )
    {
        await mailboxClient.MarkSeenAsync(settings, result.SeenUids);
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
    IReadOnlyList<uint> SeenUids
);
