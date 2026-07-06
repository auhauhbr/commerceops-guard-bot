namespace CommerceOps.Domain;

public sealed class ClientApplication
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string PublicId { get; set; }
    public required string Secret { get; set; }
    public string? BaseUrl { get; set; }
    public string? TelegramChannel { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<OperationalEvent> OperationalEvents { get; set; } = [];
}
