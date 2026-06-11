var configuration = OfferWatchConfiguration.Load();

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- samples/barnashus-reflex-70.txt [--json] [--ai]");
    Console.WriteLine("       dotnet run -- --folder samples [--json] [--ai]");
    Console.WriteLine("       dotnet run -- --mailbox [--json] [--ai]");
    return;
}

var folderIndex = Array.IndexOf(args, "--folder");
var folderMode = folderIndex >= 0;
var mailboxMode = args.Contains("--mailbox");
var folderPath = folderMode && folderIndex + 1 < args.Length
    ? args[folderIndex + 1]
    : "";
var emailPath = folderMode || mailboxMode ? "" : args[0];
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

if (!folderMode && !mailboxMode && !File.Exists(emailPath))
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
    var apiKey = OfferWatchConfiguration.GetOpenAiApiKey(configuration);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.WriteLine("OPENAI_API_KEY is required when using --ai.");
        return;
    }

    aiChecker = new AiRelevanceChecker(httpClient, apiKey, OfferWatchConfiguration.GetOpenAiModel(configuration));
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

if (mailboxMode)
{
    var mailboxSettings = OfferWatchConfiguration.GetMailboxSettings(configuration);
    if (mailboxSettings is null)
    {
        Console.WriteLine("OFFERWATCH_IMAP_USER and OFFERWATCH_IMAP_PASSWORD are required when using --mailbox.");
        return;
    }

    var stateStore = new ProcessedStateStore(".offerwatch-state.json");
    ProcessedState state;

    try
    {
        state = stateStore.Load();
    }
    catch (ProcessedStateException ex)
    {
        if (jsonOutput)
        {
            jsonOutputWriter.Write(new MailboxOutput([], ex.Message));
            return;
        }

        Console.WriteLine(ex.Message);
        return;
    }

    var processedIds = state.ProcessedMailboxMessageIds.ToHashSet();
    var mailboxClient = new MailboxClient(emailTextExtractor);
    List<MailboxEmail> emails;

    try
    {
        emails = await mailboxClient.FetchUnreadMessagesAsync(mailboxSettings, processedIds);
    }
    catch (Exception ex)
    {
        var error = CreateSafeMailboxError(ex);

        if (jsonOutput)
        {
            jsonOutputWriter.Write(new MailboxOutput([], error));
            return;
        }

        Console.WriteLine($"Mailbox error: {error}");
        return;
    }

    var mailboxResults = new List<MailboxMessageOutput>();

    foreach (var email in emails)
    {
        var shouldPrintDebugExtractedText = debugExtractedText && !jsonOutput;
        if (shouldPrintDebugExtractedText)
        {
            PrintExtractedText(email.Text);
        }

        var matches = await ProcessEmailTextAsync(email.Text, watchlist, matcher, aiChecker);
        mailboxResults.Add(new MailboxMessageOutput(
            email.MessageIdentifier,
            email.Uid,
            email.From,
            email.Subject,
            matches.Count > 0,
            matches
        ));

        processedIds.Add(email.MessageIdentifier);
    }

    state.ProcessedMailboxMessageIds = processedIds.OrderBy(id => id).ToList();
    stateStore.Save(state);

    if (jsonOutput)
    {
        jsonOutputWriter.Write(new MailboxOutput(mailboxResults));
        return;
    }

    consoleOutput.WriteMailbox(mailboxResults, aiOutput);
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
    return await ProcessEmailTextAsync(emailText, watchlist, matcher, aiChecker);
}

static async Task<List<MatchResult>> ProcessEmailTextAsync(
    string emailText,
    Watchlist watchlist,
    OfferMatcher matcher,
    AiRelevanceChecker? aiChecker
)
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

static string CreateSafeMailboxError(Exception ex)
{
    if (ex is System.Net.Sockets.SocketException || ex is HttpRequestException)
    {
        return "network_error";
    }

    if (ex is TimeoutException || ex is TaskCanceledException)
    {
        return "timeout";
    }

    if (string.IsNullOrWhiteSpace(ex.Message))
    {
        return "mailbox_error";
    }

    return ex.Message.Length <= 120
        ? ex.Message
        : ex.Message[..120];
}
