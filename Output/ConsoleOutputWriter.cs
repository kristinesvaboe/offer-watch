public class ConsoleOutputWriter
{
    public void WriteSingleFile(List<MatchResult> results, bool aiOutput)
    {
        if (results.Count == 0)
        {
            Console.WriteLine("Relevant: no");
            return;
        }

        Console.WriteLine("Relevant: yes");
        Console.WriteLine();

        foreach (var result in results)
        {
            WriteMatch(result, aiOutput);
        }
    }

    public void WriteFolder(List<FileMatchOutput> fileResults, bool aiOutput)
    {
        foreach (var fileResult in fileResults)
        {
            Console.WriteLine($"File: {fileResult.FileName}");
            Console.WriteLine($"Relevant: {(fileResult.Relevant ? "yes" : "no")}");

            if (fileResult.Matches.Count > 0)
            {
                Console.WriteLine();

                foreach (var result in fileResult.Matches)
                {
                    WriteMatch(result, aiOutput);
                }
            }

            Console.WriteLine();
        }
    }

    public void WriteMailbox(List<MailboxMessageOutput> messages, bool aiOutput)
    {
        if (messages.Count == 0)
        {
            Console.WriteLine("No new mailbox messages to process.");
            return;
        }

        foreach (var message in messages)
        {
            Console.WriteLine($"From: {message.From}");
            Console.WriteLine($"Subject: {message.Subject}");
            Console.WriteLine($"Relevant: {(message.Relevant ? "yes" : "no")}");

            if (message.Matches.Count > 0)
            {
                Console.WriteLine();

                foreach (var result in message.Matches)
                {
                    WriteMatch(result, aiOutput);
                }
            }

            Console.WriteLine();
        }
    }

    private static void WriteMatch(MatchResult result, bool aiOutput)
    {
        Console.WriteLine($"Store: {result.Store}");
        Console.WriteLine($"Matched: {result.Product}");
        Console.WriteLine($"Mode: {result.Mode}");
        Console.WriteLine($"Keywords: {string.Join(", ", result.MatchedKeywords)}");
        Console.WriteLine($"Snippet: {result.Snippet}");

        if (aiOutput)
        {
            if (result.AiAvailable == false)
            {
                Console.WriteLine("AI check: unavailable");
                Console.WriteLine($"AI reason: OpenAI API error: {result.AiError}");
            }
            else
            {
                Console.WriteLine($"AI relevant: {result.AiRelevant}");
                Console.WriteLine($"AI confidence: {result.AiConfidence}");
                Console.WriteLine($"AI reason: {result.AiReason}");
            }
        }

        if (result.NegativeKeywords.Count > 0)
        {
            Console.WriteLine($"Ignored negative keywords: {string.Join(", ", result.NegativeKeywords)}");
        }

        if (!string.IsNullOrWhiteSpace(result.Notes))
        {
            Console.WriteLine($"Note: {result.Notes}");
        }

        Console.WriteLine();
    }
}
