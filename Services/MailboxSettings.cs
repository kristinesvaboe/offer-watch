public record MailboxSettings(
    string Host,
    int Port,
    string User,
    string Password,
    bool UseSsl,
    int MaxMessages
);
