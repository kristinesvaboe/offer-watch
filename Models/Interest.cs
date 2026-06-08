public class Interest
{
    public string Product { get; set; } = "";
    public string Mode { get; set; } = "any";
    public List<string> Keywords { get; set; } = [];
    public List<string> NegativeKeywords { get; set; } = [];
    public string Notes { get; set; } = "";
}
