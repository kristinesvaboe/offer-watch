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
var useAi = aiOutput || mailboxMode;
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

if (useAi)
{
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

        Console.WriteLine("OPENAI_API_KEY is required when using --ai.");
        return;
    }

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

    consoleOutput.WriteFolder(fileResults, aiOutput);
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
    List<MailboxEmail> emails;

    try
    {
        emails = await mailboxClient.FetchUnreadMessagesAsync(mailboxSettings);
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
    var seenUids = new List<uint>();
    var relevantEmails = new List<(MailboxEmail Email, List<MatchResult> Matches)>();

    foreach (var email in emails)
    {
        var shouldPrintDebugExtractedText = debugExtractedText && !jsonOutput;
        if (shouldPrintDebugExtractedText)
        {
            PrintExtractedText(email.Text);
        }

        var matches = await offerProcessor.ProcessAsync(email.Text, watchlist);
        var aiFailed = HasAiUnavailableMatch(matches);
        var relevant = HasAiRelevantMatch(matches);

        mailboxResults.Add(new MailboxMessageOutput(
            email.MessageIdentifier,
            email.Uid,
            email.From,
            email.Subject,
            relevant,
            matches
        ));

        if (matches.Count == 0 || !aiFailed)
        {
            seenUids.Add(email.Uid);
        }

        if (relevant && !aiFailed)
        {
            relevantEmails.Add((email, matches));
        }
    }

    try
    {
        foreach (var relevantEmail in relevantEmails)
        {
            await mailboxForwarder.ForwardRelevantAsync(
                smtpSettings,
                forwardingRecipient,
                relevantEmail.Email,
                relevantEmail.Matches
            );
        }
    }
    catch (Exception ex)
    {
        var error = CreateSafeMailboxError(ex);

        if (jsonOutput)
        {
            jsonOutputWriter.Write(new MailboxOutput(mailboxResults, error));
            return;
        }

        Console.WriteLine($"Mailbox error: {error}");
        return;
    }

    if (jsonOutput)
    {
        jsonOutputWriter.Write(new MailboxOutput(mailboxResults));
        await mailboxClient.MarkSeenAsync(mailboxSettings, seenUids);
        return;
    }

    consoleOutput.WriteMailbox(mailboxResults, true);
    await mailboxClient.MarkSeenAsync(mailboxSettings, seenUids);
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

consoleOutput.WriteSingleFile(results, aiOutput);

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

static bool HasAiRelevantMatch(List<MatchResult> matches)
{
    return matches.Any(match => match.AiAvailable == true && match.AiRelevant == true);
}

static bool HasAiUnavailableMatch(List<MatchResult> matches)
{
    return matches.Any(match => match.AiAvailable == false);
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
