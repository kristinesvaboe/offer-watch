using System.Net;
using System.Text.RegularExpressions;
using MimeKit;

public class EmailTextExtractor
{
    public string ExtractText(string path)
    {
        var extension = Path.GetExtension(path);

        if (string.Equals(extension, ".eml", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractEmlText(path);
        }

        return File.ReadAllText(path);
    }

    private static string ExtractEmlText(string path)
    {
        var message = MimeMessage.Load(path);
        var body = ShouldUseHtmlBody(message.TextBody, message.HtmlBody)
            ? HtmlToText(message.HtmlBody ?? "")
            : message.TextBody ?? "";

        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"From: {message.From}",
                $"Subject: {message.Subject}",
                "",
                body
            }
        );
    }

    private static bool ShouldUseHtmlBody(string? textBody, string? htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(textBody))
        {
            return true;
        }

        var normalizedText = textBody.ToLowerInvariant();
        return normalizedText.Contains("supporting html email")
            || normalizedText.Contains("view the email online")
            || normalizedText.Contains("view this email online");
    }

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var withoutScripts = Regex.Replace(
            html,
            "<(script|style)\\b[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        var withLineBreaks = Regex.Replace(
            withoutScripts,
            "</?(p|div|br|li|tr|h[1-6])\\b[^>]*>",
            Environment.NewLine,
            RegexOptions.IgnoreCase
        );
        var withoutTags = Regex.Replace(withLineBreaks, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        var lines = decoded
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeWhitespace)
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
