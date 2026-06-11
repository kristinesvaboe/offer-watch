public class OfferProcessor
{
    private readonly OfferMatcher matcher;
    private readonly AiRelevanceChecker? aiChecker;

    public OfferProcessor(OfferMatcher matcher, AiRelevanceChecker? aiChecker)
    {
        this.matcher = matcher;
        this.aiChecker = aiChecker;
    }

    public async Task<List<MatchResult>> ProcessAsync(string emailText, Watchlist watchlist)
    {
        var results = matcher.FindMatches(watchlist, emailText);

        if (aiChecker is null || results.Count == 0)
        {
            return results;
        }

        for (var i = 0; i < results.Count; i++)
        {
            try
            {
                var aiResult = await aiChecker.CheckAsync(results[i]);
                results[i] = results[i] with
                {
                    AiAvailable = true,
                    AiRelevant = aiResult.AiRelevant,
                    AiConfidence = aiResult.AiConfidence,
                    AiReason = aiResult.AiReason
                };
            }
            catch (AiRelevanceException ex)
            {
                results[i] = results[i] with
                {
                    AiAvailable = false,
                    AiError = ex.SafeMessage
                };
            }
            catch (Exception ex)
            {
                results[i] = results[i] with
                {
                    AiAvailable = false,
                    AiError = AiRelevanceChecker.CreateSafeError(ex)
                };
            }
        }

        return results;
    }
}
