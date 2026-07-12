using CommerceOps.Application.Lumora;
using CommerceOps.Application.Triage;
using CommerceOps.Domain;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Persistence;

public sealed class OrderTriageService(
    CommerceOpsDbContext dbContext,
    ILumoraClient lumoraClient,
    IOrderRiskScorer riskScorer,
    TimeProvider timeProvider) : IOrderTriageService
{
    public async Task<OrderTriageRefreshResult> RefreshAsync(
        Guid clientApplicationId,
        int? lookbackMinutes = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var result = await lumoraClient.GetTriageCandidatesAsync(lookbackMinutes, limit, cancellationToken);
        if (!result.IsSuccess || result.Data is null)
        {
            return new OrderTriageRefreshResult(0, 0, 0, result.Error?.Code ?? "unknown_error");
        }

        var now = timeProvider.GetUtcNow();
        var upsertedCount = 0;
        var skippedCount = 0;
        var existingSnapshots = await dbContext.OrderTriageSnapshots
            .Where(snapshot => snapshot.ClientApplicationId == clientApplicationId)
            .ToDictionaryAsync(snapshot => snapshot.OrderId, cancellationToken);

        foreach (var lumoraCandidate in result.Data.Items)
        {
            if (string.IsNullOrWhiteSpace(lumoraCandidate.OrderId) ||
                string.IsNullOrWhiteSpace(lumoraCandidate.OrderStatus))
            {
                skippedCount++;
                continue;
            }

            var candidate = ToTriageCandidate(lumoraCandidate);
            var risk = riskScorer.Score(candidate, now);

            if (!existingSnapshots.TryGetValue(candidate.OrderId, out var snapshot))
            {
                snapshot = new OrderTriageSnapshot
                {
                    Id = Guid.NewGuid(),
                    ClientApplicationId = clientApplicationId,
                    OrderId = candidate.OrderId,
                    RiskLevel = risk.Level,
                    OrderStatus = candidate.OrderStatus
                };
                dbContext.OrderTriageSnapshots.Add(snapshot);
            }

            snapshot.OrderNumber = candidate.OrderNumber;
            snapshot.RiskScore = risk.Score;
            snapshot.RiskLevel = risk.Level;
            snapshot.LastFindingCode = risk.PrimaryFindingCode;
            snapshot.Summary = risk.Summary;
            snapshot.OrderStatus = candidate.OrderStatus;
            snapshot.PaymentStatus = candidate.PaymentStatus;
            snapshot.TotalValue = candidate.TotalValue;
            snapshot.SourceUpdatedAt = candidate.UpdatedAt;
            snapshot.RefreshedAt = now;
            upsertedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new OrderTriageRefreshResult(result.Data.Items.Count, upsertedCount, skippedCount);
    }

    public async Task<IReadOnlyList<OrderTriageSnapshotDetails>> GetTopAsync(
        int limit,
        int? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 50);
        var skip = Math.Max(0, cursor ?? 0);

        return await dbContext.OrderTriageSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.RiskScore >= 30)
            .OrderByDescending(snapshot => snapshot.RiskScore)
            .ThenByDescending(snapshot => snapshot.SourceUpdatedAt)
            .Skip(skip)
            .Take(safeLimit)
            .Select(snapshot => new OrderTriageSnapshotDetails(
                snapshot.Id,
                snapshot.ClientApplicationId,
                snapshot.OrderId,
                snapshot.OrderNumber,
                snapshot.RiskScore,
                snapshot.RiskLevel,
                snapshot.LastFindingCode,
                snapshot.Summary,
                snapshot.OrderStatus,
                snapshot.PaymentStatus,
                snapshot.TotalValue,
                snapshot.SourceUpdatedAt,
                snapshot.RefreshedAt,
                snapshot.Notified,
                snapshot.LastNotifiedAt))
            .ToListAsync(cancellationToken);
    }

    private static OrderTriageCandidate ToTriageCandidate(LumoraOrderTriageCandidate candidate)
    {
        return new OrderTriageCandidate(
            candidate.OrderId,
            candidate.OrderNumber,
            candidate.OrderStatus,
            candidate.PaymentStatus,
            candidate.PaymentApprovedAt,
            candidate.HasNegativeStock,
            candidate.TotalValue,
            candidate.UpdatedAt,
            candidate.Findings);
    }
}
