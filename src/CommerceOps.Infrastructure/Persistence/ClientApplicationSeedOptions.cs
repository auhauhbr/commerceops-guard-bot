namespace CommerceOps.Infrastructure.Persistence;

public sealed class ClientApplicationSeedOptions
{
    public const string SectionName = "ClientApplicationSeed";

    public string Name { get; set; } = "Lumora";
    public string PublicId { get; set; } = "lumora";
    public string Secret { get; set; } = "change-me-local-secret";
    public string? BaseUrl { get; set; }
    public string? TelegramChannel { get; set; }
    public bool IsActive { get; set; } = true;
}
