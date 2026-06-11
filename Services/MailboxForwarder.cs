using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public class MailboxForwarder
{
    public async Task ForwardRelevantAsync(
        SmtpSettings settings,
        string forwardingRecipient,
        MailboxEmail email,
        List<MatchResult> matches
    )
    {
        if (matches.Count == 0)
        {
            return;
        }

        var forwarded = new MimeMessage();
        forwarded.From.Add(MailboxAddress.Parse(settings.User));
        forwarded.To.Add(MailboxAddress.Parse(forwardingRecipient));
        forwarded.Subject = $"Offer Watch: {email.Subject}";
        forwarded.Body = CreateForwardBody(email, matches);

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(settings.User, settings.Password);
        await client.SendAsync(forwarded);
        await client.DisconnectAsync(true);
    }

    private static MimeEntity CreateForwardBody(MailboxEmail email, List<MatchResult> matches)
    {
        var multipart = new Multipart("mixed")
        {
            new TextPart("plain")
            {
                Text = CreateExplanation(email, matches)
            },
            new MessagePart
            {
                Message = email.OriginalMessage
            }
        };

        return multipart;
    }

    private static string CreateExplanation(MailboxEmail email, List<MatchResult> matches)
    {
        var lines = new List<string>
        {
            "Offer Watch found a relevant newsletter offer.",
            "",
            $"Mailbox message id: {email.MessageIdentifier}",
            $"Original From: {email.From}",
            $"Original Subject: {email.Subject}",
            ""
        };

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            lines.Add($"Match {i + 1}:");
            lines.Add($"Store: {match.Store}");
            lines.Add($"Product/interest: {match.Product}");
            lines.Add($"Matched keywords: {string.Join(", ", match.MatchedKeywords)}");
            lines.Add($"Snippet: {match.Snippet}");

            if (match.AiAvailable == true)
            {
                lines.Add($"AI relevant: {match.AiRelevant}");
                lines.Add($"AI confidence: {match.AiConfidence}");
                lines.Add($"AI reason: {match.AiReason}");
            }

            if (match.AiAvailable == false)
            {
                lines.Add($"AI check: unavailable");
                lines.Add($"AI reason: OpenAI API error: {match.AiError}");
            }

            lines.Add("");
        }

        lines.Add("Later, replies to this forwarded email may be used to correct matches or update the watchlist.");
        lines.Add("");
        lines.Add("The full original email is attached below.");

        return string.Join(Environment.NewLine, lines);
    }
}
