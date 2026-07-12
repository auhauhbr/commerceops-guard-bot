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
    IReadOnlyList<string>? Findings,
    int? ItemCount = null);

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

public sealed record OrderTriageRefreshResult(
    int CandidatesCount,
    int UpsertedCount,
    int SkippedCount,
    string? ErrorCode = null)
{
    public bool IsSuccess => ErrorCode is null;
}

public interface IOrderRiskScorer
{
    OrderRiskScore Score(OrderTriageCandidate candidate, DateTimeOffset now);
}

public interface IOrderRiskClassifier
{
    Task<AiRiskAssessmentResult> ClassifyAsync(
        OrderTriageCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public interface IOrderTriageService
{
    Task<OrderTriageRefreshResult> RefreshAsync(
        Guid clientApplicationId,
        int? lookbackMinutes = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderTriageSnapshotDetails>> GetTopAsync(
        int limit,
        int? cursor = null,
        CancellationToken cancellationToken = default);
}
