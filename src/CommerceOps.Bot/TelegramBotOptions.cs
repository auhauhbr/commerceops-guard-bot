namespace CommerceOps.Bot;

public sealed class TelegramBotOptions
{
    public const string SectionName = "Telegram";

    public string? BotToken { get; set; }

    public List<long> AllowedAdminIds { get; set; } = [];

    public static List<long> ParseAllowedAdminIds(string rawValue)
    {
        return rawValue
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }
}
