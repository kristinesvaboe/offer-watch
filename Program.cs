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
var debugExtractedText = args.Contains("--debug-extracted-text");

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

var watchlist = WatchlistLoader.Load("watchlist.yaml");
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

var matcher = new OfferMatcher(new SnippetExtractor());
var emailTextExtractor = new EmailTextExtractor();
var consoleOutput = new ConsoleOutputWriter();
var jsonOutputWriter = new JsonOutputWriter();

if (folderMode)
{
    var fileResults = new List<FileMatchOutput>();
    var files = Directory
        .EnumerateFiles(folderPath)
        .Where(IsSupportedInputFile)
        .OrderBy(Path.GetFileName)
        .ToList();

    foreach (var file in files)
    {
        var matches = await ProcessFileAsync(
            file,
            watchlist,
            matcher,
            emailTextExtractor,
            aiChecker,
            debugExtractedText && !jsonOutput ? PrintExtractedText : null
        );
        fileResults.Add(new FileMatchOutput(Path.GetFileName(file), matches.Count > 0, matches));
    }

    if (jsonOutput)
    {
        jsonOutputWriter.Write(new FolderMatchOutput(fileResults));
        return;
    }

    consoleOutput.WriteFolder(fileResults, aiOutput);
    return;
}

var results = await ProcessFileAsync(
    emailPath,
    watchlist,
    matcher,
    emailTextExtractor,
    aiChecker,
    debugExtractedText && !jsonOutput ? PrintExtractedText : null
);

if (jsonOutput)
{
    jsonOutputWriter.Write(new MatchOutput(results.Count > 0, results));
    return;
}

consoleOutput.WriteSingleFile(results, aiOutput);

static async Task<List<MatchResult>> ProcessFileAsync(
    string emailPath,
    Watchlist watchlist,
    OfferMatcher matcher,
    EmailTextExtractor emailTextExtractor,
    AiRelevanceChecker? aiChecker,
    Action<string>? debugExtractedText
)
{
    var emailText = emailTextExtractor.ExtractText(emailPath);
    debugExtractedText?.Invoke(emailText);
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

static bool IsSupportedInputFile(string path)
{
    var extension = Path.GetExtension(path);
    return string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".eml", StringComparison.OrdinalIgnoreCase);
}

static void PrintExtractedText(string text)
{
    Console.WriteLine("--- Extracted text start ---");
    Console.WriteLine(text);
    Console.WriteLine("--- Extracted text end ---");
}
