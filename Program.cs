using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- samples/barnashus-reflex-70.txt [--json] [--ai]");
    Console.WriteLine("       dotnet run -- --folder samples [--json] [--ai]");
    return;
}

var folderIndex = Array.IndexOf(args, "--folder");
var folderMode = folderIndex >= 0;
var folderPath = folderMode && folderIndex + 1 < args.Length
    ? args[folderIndex + 1]
    : "";
var emailPath = folderMode ? "" : args[0];
var jsonOutput = args.Contains("--json");
var aiOutput = args.Contains("--ai");

if (folderMode && string.IsNullOrWhiteSpace(folderPath))
{
    Console.WriteLine("--folder requires a path.");
    return;
}

if (folderMode && !Directory.Exists(folderPath))
{
    Console.WriteLine($"Folder not found: {folderPath}");
    return;
}

if (!folderMode && !File.Exists(emailPath))
{
    Console.WriteLine($"File not found: {emailPath}");
    return;
}

if (!File.Exists("watchlist.yaml"))
{
    Console.WriteLine("File not found: watchlist.yaml");
    return;
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
};

var watchlistText = File.ReadAllText("watchlist.yaml");
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var watchlist = deserializer.Deserialize<Watchlist>(watchlistText);
AiRelevanceChecker? aiChecker = null;
using var httpClient = new HttpClient();

if (aiOutput)
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.WriteLine("OPENAI_API_KEY is required when using --ai.");
        return;
    }

    aiChecker = new AiRelevanceChecker(httpClient, apiKey);
}

if (folderMode)
{
    var fileResults = new List<FileMatchOutput>();
    var files = Directory
        .EnumerateFiles(folderPath, "*.txt")
        .OrderBy(Path.GetFileName)
        .ToList();

    foreach (var file in files)
    {
        var matches = await ProcessFileAsync(file, watchlist, aiChecker);
        fileResults.Add(new FileMatchOutput(Path.GetFileName(file), matches.Count > 0, matches));
    }

    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(new FolderMatchOutput(fileResults), jsonOptions));
        return;
    }

    foreach (var fileResult in fileResults)
    {
        Console.WriteLine($"File: {fileResult.FileName}");
        Console.WriteLine($"Relevant: {(fileResult.Relevant ? "yes" : "no")}");

        if (fileResult.Matches.Count > 0)
        {
            Console.WriteLine();

            foreach (var result in fileResult.Matches)
            {
                PrintMatch(result, aiOutput);
            }
        }

        Console.WriteLine();
    }

    return;
}

var results = await ProcessFileAsync(emailPath, watchlist, aiChecker);

if (jsonOutput)
{
    var output = new MatchOutput(results.Count > 0, results);
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
    PrintMatch(result, aiOutput);
}

static async Task<List<MatchResult>> ProcessFileAsync(
    string emailPath,
    Watchlist watchlist,
    AiRelevanceChecker? aiChecker
)
{
    var emailText = File.ReadAllText(emailPath);
    var normalizedEmail = Normalize(emailText);
    var results = FindMatches(watchlist, normalizedEmail, emailText);

    if (aiChecker is not null && results.Count > 0)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var aiResult = await aiChecker.CheckAsync(results[i]);
            results[i] = results[i] with
            {
                AiRelevant = aiResult.AiRelevant,
                AiConfidence = aiResult.AiConfidence,
                AiReason = aiResult.AiReason
            };
        }
    }

    return results;
}

static void PrintMatch(MatchResult result, bool aiOutput)
{
    Console.WriteLine($"Store: {result.Store}");
    Console.WriteLine($"Matched: {result.Product}");
    Console.WriteLine($"Mode: {result.Mode}");
    Console.WriteLine($"Keywords: {string.Join(", ", result.MatchedKeywords)}");
    Console.WriteLine($"Snippet: {result.Snippet}");

    if (aiOutput)
    {
        Console.WriteLine($"AI relevant: {result.AiRelevant}");
        Console.WriteLine($"AI confidence: {result.AiConfidence}");
        Console.WriteLine($"AI reason: {result.AiReason}");
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
    string Notes,
    bool? AiRelevant = null,
    string? AiConfidence = null,
    string? AiReason = null
);

public record MatchOutput(
    bool Relevant,
    List<MatchResult> Matches
);

public record FileMatchOutput(
    string FileName,
    bool Relevant,
    List<MatchResult> Matches
);

public record FolderMatchOutput(
    List<FileMatchOutput> Files
);

public record AiRelevanceResult(
    bool AiRelevant,
    string AiConfidence,
    string AiReason
);

public class AiRelevanceChecker
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string model;

    public AiRelevanceChecker(HttpClient httpClient, string apiKey)
    {
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
    }

    public async Task<AiRelevanceResult> CheckAsync(MatchResult match)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var payload = CreatePayload(match);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        using var response = await httpClient.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"AI relevance check failed: {response.StatusCode} {responseText}");
        }

        var outputText = ExtractOutputText(responseText);
        var result = JsonSerializer.Deserialize<AiRelevanceResult>(
            outputText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (result is null)
        {
            throw new InvalidOperationException("AI relevance check returned empty JSON.");
        }

        return result;
    }

    private Dictionary<string, object?> CreatePayload(MatchResult match)
    {
        return new Dictionary<string, object?>
        {
            ["model"] = model,
            ["input"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = "You decide whether a newsletter snippet is actually a relevant offer for a configured watchlist interest. Return only JSON matching the schema."
                },
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = $"""
                    Store: {match.Store}
                    Product interest: {match.Product}
                    Matched keywords: {string.Join(", ", match.MatchedKeywords)}
                    Notes: {match.Notes}
                    Email snippet: {match.Snippet}

                    Decide if this snippet is actually a relevant offer for the product interest.
                    """
                }
            },
            ["text"] = new Dictionary<string, object?>
            {
                ["format"] = new Dictionary<string, object?>
                {
                    ["type"] = "json_schema",
                    ["name"] = "ai_relevance",
                    ["strict"] = true,
                    ["schema"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new[] { "aiRelevant", "aiConfidence", "aiReason" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["aiRelevant"] = new Dictionary<string, object?>
                            {
                                ["type"] = "boolean"
                            },
                            ["aiConfidence"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "low", "medium", "high" }
                            },
                            ["aiReason"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string"
                            }
                        }
                    }
                }
            }
        };
    }

    private static string ExtractOutputText(string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        var outputText = FindOutputText(document.RootElement);

        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("AI relevance check response did not include output text.");
        }

        return outputText;
    }

    private static string? FindOutputText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type)
                && type.GetString() == "output_text"
                && element.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }

            foreach (var property in element.EnumerateObject())
            {
                var found = FindOutputText(property.Value);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindOutputText(item);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }
}
