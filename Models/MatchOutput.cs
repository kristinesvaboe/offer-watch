public record MatchOutput(
    bool Relevant,
    List<MatchResult> Matches
);

public record FileMatchOutput(
    string FileName,
    bool Relevant,
    List<MatchResult> Matches
);

public record FolderMatchOutput(
    List<FileMatchOutput> Files
);
