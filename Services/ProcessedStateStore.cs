using System.Text.Json;

public class ProcessedStateStore
{
    private readonly string path;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };

    public ProcessedStateStore(string path)
    {
        this.path = path;
    }

    public ProcessedState Load()
    {
        if (!File.Exists(path))
        {
            return new ProcessedState();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProcessedState>(json) ?? new ProcessedState();
        }
        catch (JsonException)
        {
            throw new ProcessedStateException(
                $"{path} could not be read. It may be corrupted. Delete {path} to rebuild mailbox state."
            );
        }
    }

    public void Save(ProcessedState state)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(state, jsonOptions));
    }
}
