namespace CommerceOps.Application.Triage;

public sealed record OrderTriageCandidate(
    string OrderId,
    string? OrderNumber,
    string OrderStatus,
    string? PaymentStatus,
    DateTimeOffset? PaymentApprovedAt,
    bool HasNegativeStock,
    decimal? TotalValue,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string>? Findings);

public sealed record OrderRiskScore(
    int Score,
    string Level,
    string? PrimaryFindingCode,
    string? Summary);

public sealed record OrderTriageSnapshotDetails(
    Guid Id,
    Guid ClientApplicationId,
    string OrderId,
    string? OrderNumber,
    int RiskScore,
    string RiskLevel,
    string? LastFindingCode,
    string? Summary,
    string OrderStatus,
    string? PaymentStatus,
    decimal? TotalValue,
    DateTimeOffset SourceUpdatedAt,
    DateTimeOffset RefreshedAt,
    bool Notified,
    DateTimeOffset? LastNotifiedAt);

public interface IOrderRiskScorer
{
    OrderRiskScore Score(OrderTriageCandidate candidate, DateTimeOffset now);
}

public interface IOrderTriageService
{
    Task RefreshAsync(Guid clientApplicationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderTriageSnapshotDetails>> GetTopAsync(
        int limit,
        int? cursor = null,
        CancellationToken cancellationToken = default);
}
