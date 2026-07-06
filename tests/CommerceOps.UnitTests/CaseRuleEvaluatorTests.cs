using CommerceOps.Application.Cases;
using CommerceOps.Domain;

namespace CommerceOps.UnitTests;

public sealed class CaseRuleEvaluatorTests
{
    private readonly CaseRuleEvaluator _evaluator = new();

    [Fact]
    public void EvaluateCreatesCaseForApprovedPaymentWithPendingOrder()
    {
        var candidate = _evaluator.Evaluate(CreateEvent(
            eventType: "payment_approved",
            entityType: "order",
            dataJson: """{"order_status":"pending"}"""));

        Assert.NotNull(candidate);
        Assert.Equal("paid_order_pending", candidate.ProblemType);
        Assert.Equal("Pedido pago não confirmado", candidate.Title);
        Assert.Equal("medium", candidate.RiskLevel);
        Assert.Equal(40, candidate.RiskScore);
    }

    [Fact]
    public void EvaluateCreatesCaseForNegativeInventory()
    {
        var candidate = _evaluator.Evaluate(CreateEvent(
            eventType: "inventory_negative",
            entityType: "product",
            dataJson: """{"sku":"ABC","stock":-1}"""));

        Assert.NotNull(candidate);
        Assert.Equal("inventory_negative", candidate.ProblemType);
        Assert.Equal("Estoque negativo", candidate.Title);
        Assert.Equal("medium", candidate.RiskLevel);
        Assert.Equal(35, candidate.RiskScore);
    }

    [Fact]
    public void EvaluateDoesNotCreateCaseForNonCriticalEvent()
    {
        var candidate = _evaluator.Evaluate(CreateEvent(
            eventType: "order_created",
            entityType: "order",
            dataJson: """{"order_status":"pending"}"""));

        Assert.Null(candidate);
    }

    [Fact]
    public void EvaluateDoesNotCreatePaymentCaseWhenOrderIsNotPending()
    {
        var candidate = _evaluator.Evaluate(CreateEvent(
            eventType: "payment_approved",
            entityType: "order",
            dataJson: """{"order_status":"paid"}"""));

        Assert.Null(candidate);
    }

    private static OperationalEvent CreateEvent(string eventType, string entityType, string dataJson)
    {
        return new OperationalEvent
        {
            Id = Guid.NewGuid(),
            ClientApplicationId = Guid.NewGuid(),
            EventType = eventType,
            EntityType = entityType,
            EntityId = "1042",
            OccurredAt = DateTimeOffset.UtcNow,
            Severity = "info",
            RawBody = "{}",
            DataJson = dataJson,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }
}
