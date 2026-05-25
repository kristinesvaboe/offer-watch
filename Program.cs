using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- samples/barnashus-reflex-70.txt [--json]");
    return;
}

var emailPath = args[0];
var jsonOutput = args.Contains("--json");

if (!File.Exists(emailPath))
{
    Console.WriteLine($"File not found: {emailPath}");
    return;
}

if (!File.Exists("watchlist.yaml"))
{
    Console.WriteLine("File not found: watchlist.yaml");
    return;
}

var watchlistText = File.ReadAllText("watchlist.yaml");
var emailText = File.ReadAllText(emailPath);
var normalizedEmail = Normalize(emailText);

var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var watchlist = deserializer.Deserialize<Watchlist>(watchlistText);

var results = FindMatches(watchlist, normalizedEmail, emailText);

if (jsonOutput)
{
    var output = new MatchOutput(results.Count > 0, results);
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
    return;
}

if (results.Count == 0)
{
    Console.WriteLine("Relevant: no");
    return;
}

Console.WriteLine("Relevant: yes");
Console.WriteLine();

foreach (var result in results)
{
    Console.WriteLine($"Store: {result.Store}");
    Console.WriteLine($"Matched: {result.Product}");
    Console.WriteLine($"Mode: {result.Mode}");
    Console.WriteLine($"Keywords: {string.Join(", ", result.MatchedKeywords)}");
    Console.WriteLine($"Snippet: {result.Snippet}");

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

static List<MatchResult> FindMatches(Watchlist watchlist, string normalizedEmail, string emailText)
{
    var results = new List<MatchResult>();

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

            var matchedNegativeKeywords = interest.NegativeKeywords
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

            if (matchedNegativeKeywords.Count > 0)
            {
                continue;
            }

            results.Add(new MatchResult(
                store.Name,
                interest.Product,
                mode,
                matchedKeywords,
                CreateSnippet(emailText, matchedKeywords[0]),
                matchedNegativeKeywords,
                interest.Notes
            ));
        }
    }

    return results;
}

static string Normalize(string text)
{
    return text.ToLowerInvariant();
}

static string CreateSnippet(string text, string keyword)
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
        return string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    var start = Math.Max(0, matchIndex - contextLength);
    var end = Math.Min(text.Length, matchIndex + keyword.Length + contextLength);
    var snippet = text[start..end];

    return string.Join(" ", snippet.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public class Watchlist
{
    public List<Store> Stores { get; set; } = [];
}

public class Store
{
    public string Name { get; set; } = "";
    public List<string> SenderKeywords { get; set; } = [];
    public List<Interest> Interests { get; set; } = [];
}

public class Interest
{
    public string Product { get; set; } = "";
    public string Mode { get; set; } = "any";
    public List<string> Keywords { get; set; } = [];
    public List<string> NegativeKeywords { get; set; } = [];
    public string Notes { get; set; } = "";
}

public record MatchResult(
    string Store,
    string Product,
    string Mode,
    List<string> MatchedKeywords,
    string Snippet,
    List<string> NegativeKeywords,
    string Notes
);

public record MatchOutput(
    bool Relevant,
    List<MatchResult> Matches
);
