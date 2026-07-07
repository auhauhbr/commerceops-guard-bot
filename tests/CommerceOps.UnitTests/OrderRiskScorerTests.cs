using CommerceOps.Application.Triage;

namespace CommerceOps.UnitTests;

public sealed class OrderRiskScorerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-07T12:00:00Z");

    [Fact]
    public void ScoreReturnsMediumForApprovedPaymentStuckInPendingOrder()
    {
        var scorer = new OrderRiskScorer();
        var candidate = CreateCandidate(
            orderStatus: "pending",
            paymentStatus: "approved",
            paymentApprovedAt: Now.AddMinutes(-12));

        var score = scorer.Score(candidate, Now);

        Assert.Equal(40, score.Score);
        Assert.Equal("medium", score.Level);
        Assert.Equal("order_paid_but_pending", score.PrimaryFindingCode);
    }

    [Fact]
    public void ScoreAddsNegativeStockPoints()
    {
        var scorer = new OrderRiskScorer();
        var candidate = CreateCandidate(hasNegativeStock: true);

        var score = scorer.Score(candidate, Now);

        Assert.Equal(30, score.Score);
        Assert.Equal("medium", score.Level);
        Assert.Equal("negative_stock", score.PrimaryFindingCode);
    }

    [Fact]
    public void ScoreIncreasesWithOrderAge()
    {
        var scorer = new OrderRiskScorer();
        var candidate = CreateCandidate(updatedAt: Now.AddHours(-7));

        var score = scorer.Score(candidate, Now);

        Assert.Equal(20, score.Score);
        Assert.Equal("low", score.Level);
        Assert.Equal("stale_order", score.PrimaryFindingCode);
    }

    [Fact]
    public void ScoreKeepsNormalOrderLow()
    {
        var scorer = new OrderRiskScorer();
        var candidate = CreateCandidate(
            orderStatus: "paid",
            paymentStatus: "approved",
            updatedAt: Now.AddMinutes(-2));

        var score = scorer.Score(candidate, Now);

        Assert.Equal(0, score.Score);
        Assert.Equal("low", score.Level);
        Assert.Null(score.PrimaryFindingCode);
    }

    [Fact]
    public void ScoreHandlesNullOptionalFields()
    {
        var scorer = new OrderRiskScorer();
        var candidate = CreateCandidate(
            paymentStatus: null,
            paymentApprovedAt: null,
            totalValue: null,
            findings: null);

        var score = scorer.Score(candidate, Now);

        Assert.Equal("low", score.Level);
        Assert.True(score.Score >= 0);
    }

    private static OrderTriageCandidate CreateCandidate(
        string orderStatus = "pending",
        string? paymentStatus = "pending",
        DateTimeOffset? paymentApprovedAt = null,
        bool hasNegativeStock = false,
        decimal? totalValue = 100m,
        DateTimeOffset? updatedAt = null,
        IReadOnlyList<string>? findings = null)
    {
        return new OrderTriageCandidate(
            "1",
            "1",
            orderStatus,
            paymentStatus,
            paymentApprovedAt,
            hasNegativeStock,
            totalValue,
            updatedAt ?? Now,
            findings);
    }
}
