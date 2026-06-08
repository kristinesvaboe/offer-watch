public record MatchResult(
    string Store,
    string Product,
    string Mode,
    List<string> MatchedKeywords,
    string Snippet,
    List<string> NegativeKeywords,
    string Notes,
    bool? AiAvailable = null,
    bool? AiRelevant = null,
    string? AiConfidence = null,
    string? AiReason = null,
    string? AiError = null
);
