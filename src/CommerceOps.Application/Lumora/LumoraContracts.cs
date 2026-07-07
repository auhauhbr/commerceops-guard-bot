using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommerceOps.Application.Lumora;

public sealed record LumoraHealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("checked_at")] DateTimeOffset? CheckedAt,
    [property: JsonPropertyName("database")] LumoraComponentHealth? Database,
    [property: JsonPropertyName("queue")] LumoraComponentHealth? Queue);

public sealed record LumoraComponentHealth(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message);

public sealed record LumoraOrderDiagnosticResponse(
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("payment_status")] string? PaymentStatus,
    [property: JsonPropertyName("stock_status")] string? StockStatus,
    [property: JsonPropertyName("findings")] IReadOnlyList<LumoraDiagnosticFinding> Findings);

public sealed record LumoraDiagnosticFinding(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("evidence")] JsonElement? Evidence);

public sealed record LumoraPaymentInconsistenciesResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LumoraPaymentInconsistency> Items);

public sealed record LumoraPaymentInconsistency(
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("payment_id")] string? PaymentId,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("provider_status")] string? ProviderStatus,
    [property: JsonPropertyName("local_status")] string? LocalStatus,
    [property: JsonPropertyName("message")] string Message);

public sealed record LumoraInventoryInconsistenciesResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LumoraInventoryInconsistency> Items);

public sealed record LumoraInventoryInconsistency(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("product_id")] string? ProductId,
    [property: JsonPropertyName("available_quantity")] int? AvailableQuantity,
    [property: JsonPropertyName("reserved_quantity")] int? ReservedQuantity,
    [property: JsonPropertyName("message")] string Message);

public sealed record LumoraDatabaseIntegrityResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("checks")] IReadOnlyList<LumoraDatabaseIntegrityCheck> Checks);

public sealed record LumoraDatabaseIntegrityCheck(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message);

public sealed record LumoraSlowQueriesResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LumoraSlowQuery> Items);

public sealed record LumoraSlowQuery(
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset? OccurredAt,
    [property: JsonPropertyName("source")] string? Source);

public sealed record LumoraFailedJobsResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LumoraFailedJob> Items);

public sealed record LumoraFailedJob(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("queue")] string? Queue,
    [property: JsonPropertyName("failed_at")] DateTimeOffset? FailedAt,
    [property: JsonPropertyName("exception")] string? Exception);
