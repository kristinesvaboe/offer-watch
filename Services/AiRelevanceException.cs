public class AiRelevanceException : Exception
{
    public AiRelevanceException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
