namespace CommerceOps.Domain;

public sealed class OrderTriageSnapshot
{
    public Guid Id { get; set; }
    public Guid ClientApplicationId { get; set; }
    public required string OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public int RiskScore { get; set; }
    public required string RiskLevel { get; set; }
    public string? LastFindingCode { get; set; }
    public string? Summary { get; set; }
    public required string OrderStatus { get; set; }
    public string? PaymentStatus { get; set; }
    public decimal? TotalValue { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public DateTimeOffset RefreshedAt { get; set; }
    public bool Notified { get; set; }
    public DateTimeOffset? LastNotifiedAt { get; set; }

    public ClientApplication? ClientApplication { get; set; }
}
