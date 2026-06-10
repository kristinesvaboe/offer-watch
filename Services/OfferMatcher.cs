public class OfferMatcher
{
    private readonly SnippetExtractor snippetExtractor;

    public OfferMatcher(SnippetExtractor snippetExtractor)
    {
        this.snippetExtractor = snippetExtractor;
    }

    public List<MatchResult> FindMatches(Watchlist watchlist, string emailText)
    {
        var results = new List<MatchResult>();
        var normalizedEmail = Normalize(emailText);

        foreach (var store in watchlist.Stores)
        {
            var storeMatched = store.SenderKeywords
                .Any(keyword => normalizedEmail.Contains(Normalize(keyword)));

            if (!storeMatched)
            {
                continue;
            }

            foreach (var interest in store.Interests)
            {
                var matchedKeywords = interest.Keywords
                    .Where(keyword => normalizedEmail.Contains(Normalize(keyword)))
                    .ToList();

                var mode = string.IsNullOrWhiteSpace(interest.Mode)
                    ? "any"
                    : interest.Mode.ToLowerInvariant();

                var isMatch = mode switch
                {
                    "all" => matchedKeywords.Count == interest.Keywords.Count,
                    "any" => matchedKeywords.Count > 0,
                    _ => matchedKeywords.Count > 0
                };

                if (!isMatch)
                {
                    continue;
                }

                var negativeKeywordContext = CreateNegativeKeywordContext(emailText, matchedKeywords[0]);
                var matchedNegativeKeywords = interest.NegativeKeywords
                    .Where(keyword => negativeKeywordContext.Contains(Normalize(keyword)))
                    .ToList();

                if (matchedNegativeKeywords.Count > 0)
                {
                    continue;
                }

                results.Add(new MatchResult(
                    store.Name,
                    interest.Product,
                    mode,
                    matchedKeywords,
                    snippetExtractor.CreateSnippet(emailText, matchedKeywords[0]),
                    matchedNegativeKeywords,
                    interest.Notes
                ));
            }
        }

        return results;
    }

    private static string Normalize(string text)
    {
        return text.ToLowerInvariant();
    }

    private static string CreateNegativeKeywordContext(string text, string keyword)
    {
        const int contextLength = 250;

        var matchIndex = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0)
        {
            return "";
        }

        var start = Math.Max(0, matchIndex - contextLength);
        var end = Math.Min(text.Length, matchIndex + keyword.Length + contextLength);

        return Normalize(text[start..end]);
    }
}
