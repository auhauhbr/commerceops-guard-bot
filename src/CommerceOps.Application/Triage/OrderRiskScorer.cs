namespace CommerceOps.Application.Triage;

public sealed class OrderRiskScorer : IOrderRiskScorer
{
    private const int PaymentApprovedPendingScore = 70;
    private const int NegativeStockScore = 70;
    private const int OrderTotalMismatchScore = 50;
    private const int PendingWithoutApprovedPaymentScore = 40;
    private const int PaymentMissingScore = 30;
    private const int HighValuePendingScore = 20;
    private const int MaxAgingScore = 30;

    private static readonly IReadOnlyDictionary<string, int> FindingScores =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_paid_but_pending"] = PaymentApprovedPendingScore,
            ["negative_stock"] = NegativeStockScore,
            ["order_total_mismatch"] = OrderTotalMismatchScore,
            ["pending_order_without_approved_payment"] = PendingWithoutApprovedPaymentScore,
            ["payment_missing"] = PaymentMissingScore
        };

    private static readonly IReadOnlyDictionary<string, int> FindingPriorities =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_paid_but_pending"] = 500,
            ["negative_stock"] = 490,
            ["order_total_mismatch"] = 400,
            ["pending_order_without_approved_payment"] = 300,
            ["payment_missing"] = 290,
            ["high_value_pending_order"] = 200,
            ["stale_order"] = 100
        };

    public OrderRiskScore Score(OrderTriageCandidate candidate, DateTimeOffset now)
    {
        var contributions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in candidate.Findings ?? [])
        {
            if (FindingScores.TryGetValue(finding, out var findingScore))
            {
                contributions[finding] = findingScore;
            }
        }

        if (HasApprovedPaymentStuckInPendingOrder(candidate, now))
        {
            contributions["order_paid_but_pending"] = PaymentApprovedPendingScore;
        }

        if (candidate.HasNegativeStock)
        {
            contributions["negative_stock"] = NegativeStockScore;
        }

        if (IsPendingStatus(candidate.OrderStatus) && candidate.TotalValue is >= 500m)
        {
            contributions["high_value_pending_order"] = HighValuePendingScore;
        }

        var agingScore = CalculateAgingScore(candidate.UpdatedAt, now);
        if (agingScore > 0)
        {
            contributions["stale_order"] = agingScore;
        }

        CollapseMissingPaymentFindings(contributions);

        var score = Math.Clamp(contributions.Values.Sum(), 0, 100);
        var primaryFinding = SelectPrimaryFinding(contributions, candidate.Findings);

        return new OrderRiskScore(
            score,
            GetRiskLevel(score),
            primaryFinding,
            FormatSummary(primaryFinding));
    }

    private static void CollapseMissingPaymentFindings(IDictionary<string, int> contributions)
    {
        if (contributions.ContainsKey("pending_order_without_approved_payment"))
        {
            contributions.Remove("payment_missing");
        }
    }

    private static string? SelectPrimaryFinding(
        IReadOnlyDictionary<string, int> contributions,
        IReadOnlyList<string>? findings)
    {
        var primaryFinding = contributions.Keys
            .OrderByDescending(code => FindingPriorities.GetValueOrDefault(code))
            .FirstOrDefault();

        return primaryFinding ?? findings?.FirstOrDefault();
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
            "order_total_mismatch" => "total do pedido divergente do valor esperado",
            "pending_order_without_approved_payment" => "pedido pendente sem pagamento aprovado",
            "payment_missing" => "pagamento não encontrado para o pedido",
            "high_value_pending_order" => "pedido de valor alto ainda pendente",
            "stale_order" => "pedido sem atualização recente",
            _ => findingCode?.Replace('_', ' ')
        };
    }
}
