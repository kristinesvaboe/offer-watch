using Microsoft.Extensions.Configuration;

public static class OfferWatchConfiguration
{
    public static IConfigurationRoot Load()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings-local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    public static string? GetOpenAiApiKey(IConfiguration configuration)
    {
        return FirstNonEmpty(
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            configuration["OfferWatch:OpenAI:ApiKey"]
        );
    }

    public static string? GetOpenAiModel(IConfiguration configuration)
    {
        return FirstNonEmpty(
            Environment.GetEnvironmentVariable("OPENAI_MODEL"),
            configuration["OfferWatch:OpenAI:Model"]
        );
    }

    public static MailboxSettings? GetMailboxSettings(IConfiguration configuration)
    {
        var user = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OFFERWATCH_IMAP_USER"),
            configuration["OfferWatch:Imap:User"]
        );
        var password = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OFFERWATCH_IMAP_PASSWORD"),
            configuration["OfferWatch:Imap:Password"]
        );

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var host = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OFFERWATCH_IMAP_HOST"),
            configuration["OfferWatch:Imap:Host"],
            "imap.gmail.com"
        );

        return new MailboxSettings(
            host,
            GetInt(
                Environment.GetEnvironmentVariable("OFFERWATCH_IMAP_PORT"),
                configuration["OfferWatch:Imap:Port"],
                993
            ),
            user,
            password,
            true,
            GetInt(
                Environment.GetEnvironmentVariable("OFFERWATCH_MAILBOX_MAX_MESSAGES"),
                configuration["OfferWatch:Mailbox:MaxMessages"],
                20
            )
        );
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static int GetInt(string? environmentValue, string? configurationValue, int defaultValue)
    {
        if (int.TryParse(environmentValue, out var environmentResult))
        {
            return environmentResult;
        }

        return int.TryParse(configurationValue, out var configurationResult)
            ? configurationResult
            : defaultValue;
    }
}
