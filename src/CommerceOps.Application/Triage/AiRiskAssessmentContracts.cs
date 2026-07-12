using System.Text.Json.Serialization;

namespace CommerceOps.Application.Triage;

public sealed record AiRiskAssessmentRequest(
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("order_status")] string OrderStatus,
    [property: JsonPropertyName("payment_status")] string? PaymentStatus,
    [property: JsonPropertyName("total_value")] decimal? TotalValue,
    [property: JsonPropertyName("item_count")] int? ItemCount,
    [property: JsonPropertyName("has_negative_stock")] bool HasNegativeStock,
    [property: JsonPropertyName("findings")] IReadOnlyList<string> Findings,
    [property: JsonPropertyName("derived_signals")] IReadOnlyDictionary<string, bool> DerivedSignals);

public sealed record AiRiskAssessmentResult(
    [property: JsonPropertyName("risk_level")] string RiskLevel,
    [property: JsonPropertyName("risk_score")] int RiskScore,
    [property: JsonPropertyName("primary_finding_code")] string? PrimaryFindingCode,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("reasoning_summary")] string? ReasoningSummary,
    [property: JsonPropertyName("recommended_action")] string? RecommendedAction,
    [property: JsonPropertyName("customer_message_subject")] string? CustomerMessageSubject,
    [property: JsonPropertyName("customer_message_body")] string? CustomerMessageBody,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonIgnore] string RiskSource = "ai");

public interface IAiRiskAssessmentProvider
{
    Task<string> AssessAsync(AiRiskAssessmentRequest request, CancellationToken cancellationToken = default);
}
