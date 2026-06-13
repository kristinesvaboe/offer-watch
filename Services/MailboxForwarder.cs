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
            CreateOpeningSentence(matches),
            ""
        };

        if (matches.Count == 1)
        {
            var match = matches[0];
            lines.Add("The part that matched was:");
            lines.Add("");
            lines.Add($"\"{match.Snippet}\"");
            lines.Add("");
            lines.Add(CreateConfidenceSentence(match));
        }
        else
        {
            lines.Add("A few parts looked relevant:");
            lines.Add("");

            foreach (var match in matches)
            {
                lines.Add($"- {match.Store} looked like it had an offer on {FormatOfferType(match.Product)}: \"{match.Snippet}\"");
                lines.Add($"  {CreateConfidenceSentence(match)}");
            }
        }

        lines.Add("");
        lines.Add("The original newsletter is attached below.");
        lines.Add("");
        lines.Add("Reference for future feedback:");
        lines.Add($"Mailbox message id: {email.MessageIdentifier}");
        lines.Add($"Original subject: {email.Subject}");
        lines.Add("");
        lines.Add("Later, replies to this forwarded email may be used to correct matches or update the watchlist.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateOpeningSentence(List<MatchResult> matches)
    {
        if (matches.Count == 1)
        {
            var match = matches[0];
            return $"Offer Watch forwarded this because it looks like {match.Store} has an offer on {FormatOfferType(match.Product)}.";
        }

        var stores = string.Join(", ", matches.Select(match => match.Store).Distinct());
        return $"Offer Watch forwarded this because it found a few offers that look relevant from {stores}.";
    }

    private static string CreateConfidenceSentence(MatchResult match)
    {
        if (match.AiAvailable == true)
        {
            var confidence = string.IsNullOrWhiteSpace(match.AiConfidence)
                ? "available"
                : match.AiConfidence;

            return match.AiRelevant == true
                ? $"The AI assessment also considered this relevant with {confidence} confidence."
                : $"The AI assessment was not confident this was relevant.";
        }

        return "The AI assessment was not available for this item.";
    }

    private static string FormatOfferType(string product)
    {
        if (string.Equals(product, "Barn og baby", StringComparison.OrdinalIgnoreCase))
        {
            return "baby and children's items";
        }

        return product.ToLowerInvariant();
    }
}
