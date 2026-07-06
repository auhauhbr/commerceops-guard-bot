using System.Text.Json;
using CommerceOps.Domain;

namespace CommerceOps.Application.Cases;

public sealed class CaseRuleEvaluator
{
    public CaseCreationCandidate? Evaluate(OperationalEvent operationalEvent)
    {
        if (IsPaidOrderStillPending(operationalEvent))
        {
            return new CaseCreationCandidate(
                ProblemType: "paid_order_pending",
                Title: "Pedido pago não confirmado",
                Summary: $"Pagamento aprovado para {operationalEvent.EntityType} {operationalEvent.EntityId}, mas o pedido permanece pendente.",
                RiskLevel: "medium",
                RiskScore: 40,
                FindingType: "payment_order_status_mismatch",
                FindingSeverity: "medium",
                FindingTitle: "Pagamento aprovado com pedido pendente",
                FindingDescription: "O evento indica pagamento aprovado enquanto o status do pedido ainda está pendente.",
                EvidenceJson: BuildEvidenceJson(operationalEvent));
        }

        if (string.Equals(operationalEvent.EventType, "inventory_negative", StringComparison.Ordinal))
        {
            return new CaseCreationCandidate(
                ProblemType: "inventory_negative",
                Title: "Estoque negativo",
                Summary: $"Estoque negativo detectado para {operationalEvent.EntityType} {operationalEvent.EntityId}.",
                RiskLevel: "medium",
                RiskScore: 35,
                FindingType: "inventory_negative",
                FindingSeverity: "medium",
                FindingTitle: "Estoque negativo detectado",
                FindingDescription: "O evento indica que o saldo de estoque ficou abaixo de zero.",
                EvidenceJson: BuildEvidenceJson(operationalEvent));
        }

        return null;
    }

    private static bool IsPaidOrderStillPending(OperationalEvent operationalEvent)
    {
        if (!string.Equals(operationalEvent.EventType, "payment_approved", StringComparison.Ordinal) ||
            !string.Equals(operationalEvent.EntityType, "order", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(operationalEvent.DataJson))
        {
            return false;
        }

        using var document = JsonDocument.Parse(operationalEvent.DataJson);
        return document.RootElement.TryGetProperty("order_status", out var orderStatus) &&
            orderStatus.ValueKind == JsonValueKind.String &&
            string.Equals(orderStatus.GetString(), "pending", StringComparison.Ordinal);
    }

    private static string BuildEvidenceJson(OperationalEvent operationalEvent)
    {
        JsonElement? data = null;
        if (!string.IsNullOrWhiteSpace(operationalEvent.DataJson))
        {
            using var document = JsonDocument.Parse(operationalEvent.DataJson);
            data = document.RootElement.Clone();
        }

        return JsonSerializer.Serialize(new
        {
            event_id = operationalEvent.Id,
            event_type = operationalEvent.EventType,
            entity_type = operationalEvent.EntityType,
            entity_id = operationalEvent.EntityId,
            occurred_at = operationalEvent.OccurredAt,
            severity = operationalEvent.Severity,
            data
        });
    }
}
