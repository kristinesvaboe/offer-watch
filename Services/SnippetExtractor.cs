public class SnippetExtractor
{
    public string CreateSnippet(string text, string keyword)
    {
        const int contextLength = 60;

        var matchIndex = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0)
        {
            return "";
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            var lineStart = text.LastIndexOfAny(['\r', '\n'], matchIndex);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            var lineEnd = text.IndexOfAny(['\r', '\n'], matchIndex);
            lineEnd = lineEnd < 0 ? text.Length : lineEnd;

            var line = text[lineStart..lineEnd];
            return NormalizeWhitespace(line);
        }

        var start = Math.Max(0, matchIndex - contextLength);
        var end = Math.Min(text.Length, matchIndex + keyword.Length + contextLength);
        var snippet = text[start..end];

        return NormalizeWhitespace(snippet);
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
