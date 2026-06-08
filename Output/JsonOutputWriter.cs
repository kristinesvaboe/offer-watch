using System.Text.Json;
using System.Text.Json.Serialization;

public class JsonOutputWriter
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public void Write<T>(T output)
    {
        Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
    }
}
