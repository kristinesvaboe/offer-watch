public record MailboxMessageOutput(
    string MessageIdentifier,
    uint Uid,
    string From,
    string Subject,
    bool Relevant,
    List<MatchResult> Matches
);

public record MailboxOutput(
    List<MailboxMessageOutput> Messages,
    string? Error = null
);
