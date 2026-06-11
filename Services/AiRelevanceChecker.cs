using System.Text.Json;

public class AiRelevanceChecker
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string model;

    public AiRelevanceChecker(HttpClient httpClient, string apiKey, string? model = null)
    {
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        this.model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
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
            throw new AiRelevanceException(ExtractErrorCode(responseText) ?? response.StatusCode.ToString());
        }

        var outputText = ExtractOutputText(responseText);
        var result = JsonSerializer.Deserialize<AiRelevanceResult>(
            outputText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (result is null)
        {
            throw new AiRelevanceException("empty_response");
        }

        return result;
    }

    public static string CreateSafeError(Exception ex)
    {
        if (ex is HttpRequestException)
        {
            return "network_error";
        }

        if (ex is TaskCanceledException)
        {
            return "timeout";
        }

        if (ex is JsonException)
        {
            return "invalid_response";
        }

        if (string.IsNullOrWhiteSpace(ex.Message))
        {
            return "unknown_error";
        }

        return ex.Message.Length <= 120
            ? ex.Message
            : ex.Message[..120];
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
            throw new AiRelevanceException("missing_output_text");
        }

        return outputText;
    }

    private static string? ExtractErrorCode(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var code)
                    && !string.IsNullOrWhiteSpace(code.GetString()))
                {
                    return code.GetString();
                }

                if (error.TryGetProperty("type", out var type)
                    && !string.IsNullOrWhiteSpace(type.GetString()))
                {
                    return type.GetString();
                }

                if (error.TryGetProperty("message", out var message)
                    && !string.IsNullOrWhiteSpace(message.GetString()))
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
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
