using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- samples/barnashus-reflex-70.txt");
    return;
}

var emailPath = args[0];

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

var results = FindMatches(watchlist, normalizedEmail);

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

static List<MatchResult> FindMatches(Watchlist watchlist, string normalizedEmail)
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
    List<string> NegativeKeywords,
    string Notes
);