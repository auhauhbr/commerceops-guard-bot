using CommerceOps.Application.Lumora;

namespace CommerceOps.Application.Actions;

public static class ActionRequestTypes
{
    public const string CustomerMessageEmail = "customer_message_email";
}

public static class ActionRequestStatuses
{
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Cancelled = "cancelled";
    public const string Executed = "executed";
    public const string Failed = "failed";
}

public sealed record CreateCustomerMessageActionRequest(
    CustomerMessageDraft Draft,
    string OrderId,
    string? OrderNumber,
    long CreatedByChatId);

public sealed record ActionRequestDetails(
    Guid Id,
    string PublicId,
    string Type,
    string Status,
    string EntityType,
    string EntityId,
    string? Risk,
    string Reason,
    string PayloadJson,
    long CreatedByChatId,
    long? ApprovedByChatId,
    long? CancelledByChatId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? ExecutedAt,
    string? FailureReason);

public interface IActionRequestService
{
    Task<ActionRequestDetails> CreateCustomerMessageEmailAsync(
        CreateCustomerMessageActionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActionRequestDetails>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<ActionRequestDetails?> ApproveAsync(
        string publicId,
        long approvedByChatId,
        CancellationToken cancellationToken = default);

    Task<ActionRequestDetails?> CancelAsync(
        string publicId,
        long cancelledByChatId,
        CancellationToken cancellationToken = default);
}
