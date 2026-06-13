var configuration = OfferWatchConfiguration.Load();

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- --mailbox [--json]");
    Console.WriteLine("       dotnet run -- samples/barnashus-reflex-70.txt [--json]");
    Console.WriteLine("       dotnet run -- --folder samples [--json]");
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
var consoleOutput = new ConsoleOutputWriter();
var jsonOutputWriter = new JsonOutputWriter();
var apiKey = OfferWatchConfiguration.GetOpenAiApiKey(configuration);

if (string.IsNullOrWhiteSpace(apiKey))
{
    if (mailboxMode)
    {
        WriteMailboxConfigurationError(
            "OfferWatch:OpenAI:ApiKey or OPENAI_API_KEY is required when using --mailbox.",
            jsonOutput,
            jsonOutputWriter
        );
        return;
    }
}
else
{
    aiChecker = new AiRelevanceChecker(httpClient, apiKey, OfferWatchConfiguration.GetOpenAiModel(configuration));
}

var matcher = new OfferMatcher(new SnippetExtractor());
var offerProcessor = new OfferProcessor(matcher, aiChecker);
var emailTextExtractor = new EmailTextExtractor();

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
            offerProcessor,
            emailTextExtractor,
            debugExtractedText && !jsonOutput ? PrintExtractedText : null
        );
        fileResults.Add(new FileMatchOutput(Path.GetFileName(file), matches.Count > 0, matches));
    }

    if (jsonOutput)
    {
        jsonOutputWriter.Write(new FolderMatchOutput(fileResults));
        return;
    }

    consoleOutput.WriteFolder(fileResults, aiChecker is not null);
    return;
}

if (mailboxMode)
{
    var mailboxSettings = OfferWatchConfiguration.GetMailboxSettings(configuration);
    if (mailboxSettings is null)
    {
        WriteMailboxConfigurationError(
            "OFFERWATCH_IMAP_USER and OFFERWATCH_IMAP_PASSWORD are required when using --mailbox.",
            jsonOutput,
            jsonOutputWriter
        );
        return;
    }

    var forwardingRecipient = OfferWatchConfiguration.GetForwardingRecipient(configuration);
    if (string.IsNullOrWhiteSpace(forwardingRecipient))
    {
        WriteMailboxConfigurationError(
            "OfferWatch:Forwarding:To or OFFERWATCH_FORWARD_TO is required when using --mailbox.",
            jsonOutput,
            jsonOutputWriter
        );
        return;
    }

    var smtpSettings = OfferWatchConfiguration.GetSmtpSettings(configuration);
    if (smtpSettings is null)
    {
        WriteMailboxConfigurationError(
            "OfferWatch:Smtp:User and OfferWatch:Smtp:Password, or OFFERWATCH_SMTP_USER and OFFERWATCH_SMTP_PASSWORD, are required when using --mailbox.",
            jsonOutput,
            jsonOutputWriter
        );
        return;
    }

    var mailboxClient = new MailboxClient(emailTextExtractor);
    var mailboxForwarder = new MailboxForwarder();
    var mailboxProcessor = new MailboxProcessor(mailboxClient, mailboxForwarder, offerProcessor);

    if (!jsonOutput)
    {
        Console.WriteLine("Mailbox run started.");
    }

    var mailboxResult = await mailboxProcessor.ProcessAsync(
        mailboxSettings,
        smtpSettings,
        forwardingRecipient,
        watchlist,
        debugExtractedText && !jsonOutput ? PrintExtractedText : null
    );

    if (mailboxResult.Output.Error is not null)
    {
        if (jsonOutput)
        {
            jsonOutputWriter.Write(mailboxResult.Output);
            return;
        }

        MailboxProcessor.WriteRunLog(mailboxResult);
        Console.WriteLine($"Mailbox error: {mailboxResult.Output.Error}");
        return;
    }

    if (jsonOutput)
    {
        jsonOutputWriter.Write(mailboxResult.Output);
        return;
    }

    MailboxProcessor.WriteRunLog(mailboxResult);
    return;
}

var results = await ProcessFileAsync(
    emailPath,
    watchlist,
    offerProcessor,
    emailTextExtractor,
    debugExtractedText && !jsonOutput ? PrintExtractedText : null
);

if (jsonOutput)
{
    jsonOutputWriter.Write(new MatchOutput(results.Count > 0, results));
    return;
}

consoleOutput.WriteSingleFile(results, aiChecker is not null);

static async Task<List<MatchResult>> ProcessFileAsync(
    string emailPath,
    Watchlist watchlist,
    OfferProcessor offerProcessor,
    EmailTextExtractor emailTextExtractor,
    Action<string>? debugExtractedText
)
{
    var emailText = emailTextExtractor.ExtractText(emailPath);
    debugExtractedText?.Invoke(emailText);
    return await offerProcessor.ProcessAsync(emailText, watchlist);
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

static void WriteMailboxConfigurationError(string message, bool jsonOutput, JsonOutputWriter jsonOutputWriter)
{
    if (jsonOutput)
    {
        jsonOutputWriter.Write(new MailboxOutput([], message));
        return;
    }

    Console.WriteLine(message);
}
