namespace CommerceOps.Application.Triage;

public sealed class OrderRiskScorer : IOrderRiskScorer
{
    private const int PaymentApprovedPendingScore = 40;
    private const int NegativeStockScore = 30;
    private const int HighValuePendingScore = 20;
    private const int MaxAgingScore = 30;

    public OrderRiskScore Score(OrderTriageCandidate candidate, DateTimeOffset now)
    {
        var score = 0;
        string? primaryFinding = null;

        if (HasApprovedPaymentStuckInPendingOrder(candidate, now))
        {
            score += PaymentApprovedPendingScore;
            primaryFinding ??= "order_paid_but_pending";
        }

        if (candidate.HasNegativeStock)
        {
            score += NegativeStockScore;
            primaryFinding ??= "negative_stock";
        }

        if (IsPendingStatus(candidate.OrderStatus) && candidate.TotalValue is >= 500m)
        {
            score += HighValuePendingScore;
            primaryFinding ??= "high_value_pending_order";
        }

        var agingScore = CalculateAgingScore(candidate.UpdatedAt, now);
        score += agingScore;
        if (agingScore > 0)
        {
            primaryFinding ??= "stale_order";
        }

        primaryFinding ??= candidate.Findings?.FirstOrDefault();

        return new OrderRiskScore(
            Math.Max(0, score),
            GetRiskLevel(score),
            primaryFinding,
            FormatSummary(primaryFinding));
    }

    private static bool HasApprovedPaymentStuckInPendingOrder(OrderTriageCandidate candidate, DateTimeOffset now)
    {
        if (!IsApprovedPayment(candidate.PaymentStatus) || !IsPendingStatus(candidate.OrderStatus))
        {
            return false;
        }

        if (candidate.PaymentApprovedAt is null)
        {
            return false;
        }

        return now - candidate.PaymentApprovedAt.Value >= TimeSpan.FromMinutes(10);
    }

    private static int CalculateAgingScore(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        if (updatedAt >= now)
        {
            return 0;
        }

        var age = now - updatedAt;
        if (age >= TimeSpan.FromHours(24))
        {
            return MaxAgingScore;
        }

        if (age >= TimeSpan.FromHours(6))
        {
            return 20;
        }

        if (age >= TimeSpan.FromMinutes(30))
        {
            return 10;
        }

        return 0;
    }

    private static bool IsPendingStatus(string? status)
    {
        return string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "pending_payment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApprovedPayment(string? paymentStatus)
    {
        return string.Equals(paymentStatus, "approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(paymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRiskLevel(int score)
    {
        if (score >= 70)
        {
            return "high";
        }

        if (score >= 30)
        {
            return "medium";
        }

        return "low";
    }

    private static string? FormatSummary(string? findingCode)
    {
        return findingCode switch
        {
            "order_paid_but_pending" => "pagamento aprovado, mas pedido ainda pendente",
            "negative_stock" => "estoque negativo em item do pedido",
            "high_value_pending_order" => "pedido de valor alto ainda pendente",
            "stale_order" => "pedido sem atualização recente",
            _ => findingCode?.Replace('_', ' ')
        };
    }
}
