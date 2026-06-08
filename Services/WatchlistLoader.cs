using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public static class WatchlistLoader
{
    public static Watchlist Load(string path)
    {
        var watchlistText = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<Watchlist>(watchlistText);
    }
}
