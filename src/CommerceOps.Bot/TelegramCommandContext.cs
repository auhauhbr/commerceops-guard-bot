namespace CommerceOps.Bot;

public sealed record TelegramCommandContext(
    long ChatId,
    long UserId,
    string Text);
