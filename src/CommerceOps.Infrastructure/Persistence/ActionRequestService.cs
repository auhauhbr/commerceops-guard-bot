using System.Text.Json;
using System.Text.Json.Serialization;
using CommerceOps.Application.Actions;
using CommerceOps.Application.Lumora;
using CommerceOps.Domain;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Persistence;

public sealed class ActionRequestService(CommerceOpsDbContext dbContext, TimeProvider timeProvider)
    : IActionRequestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ActionRequestDetails> CreateCustomerMessageEmailAsync(
        CreateCustomerMessageActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var actionRequest = new ActionRequest
        {
            Id = Guid.NewGuid(),
            PublicId = await CreatePublicIdAsync(cancellationToken),
            Type = ActionRequestTypes.CustomerMessageEmail,
            Status = ActionRequestStatuses.PendingApproval,
            EntityType = "order",
            EntityId = request.OrderId,
            Risk = request.Draft.Risk,
            Reason = request.Draft.Reason,
            PayloadJson = CreateCustomerMessagePayloadJson(request),
            CreatedByChatId = request.CreatedByChatId,
            CreatedAt = now
        };

        dbContext.ActionRequests.Add(actionRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Map(actionRequest);
    }

    public async Task<IReadOnlyList<ActionRequestDetails>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 50);
        var actionRequests = await dbContext.ActionRequests
            .AsNoTracking()
            .Where(actionRequest => actionRequest.Status == ActionRequestStatuses.PendingApproval)
            .OrderByDescending(actionRequest => actionRequest.PublicId)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);

        return actionRequests.Select(Map).ToList();
    }

    public async Task<ActionRequestDetails?> ApproveAsync(
        string publicId,
        long approvedByChatId,
        CancellationToken cancellationToken = default)
    {
        var actionRequest = await FindPendingAsync(publicId, cancellationToken);
        if (actionRequest is null)
        {
            return null;
        }

        actionRequest.Status = ActionRequestStatuses.Approved;
        actionRequest.ApprovedByChatId = approvedByChatId;
        actionRequest.ApprovedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(actionRequest);
    }

    public async Task<ActionRequestDetails?> CancelAsync(
        string publicId,
        long cancelledByChatId,
        CancellationToken cancellationToken = default)
    {
        var actionRequest = await FindPendingAsync(publicId, cancellationToken);
        if (actionRequest is null)
        {
            return null;
        }

        actionRequest.Status = ActionRequestStatuses.Cancelled;
        actionRequest.CancelledByChatId = cancelledByChatId;
        actionRequest.CancelledAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(actionRequest);
    }

    private async Task<ActionRequest?> FindPendingAsync(string publicId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return null;
        }

        var normalizedPublicId = publicId.Trim().ToUpperInvariant();

        return await dbContext.ActionRequests.SingleOrDefaultAsync(
            actionRequest =>
                actionRequest.PublicId == normalizedPublicId &&
                actionRequest.Status == ActionRequestStatuses.PendingApproval,
            cancellationToken);
    }

    private async Task<string> CreatePublicIdAsync(CancellationToken cancellationToken)
    {
        var nextNumber = await dbContext.ActionRequests.CountAsync(cancellationToken) + 1;
        return $"ACT-{nextNumber:00000}";
    }

    private static string CreateCustomerMessagePayloadJson(CreateCustomerMessageActionRequest request)
    {
        var payload = new CustomerMessageActionPayload(
            "email",
            request.Draft.Subject,
            request.Draft.Body,
            request.OrderId,
            request.OrderNumber,
            request.Draft.Findings,
            "draft_only");

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static ActionRequestDetails Map(ActionRequest actionRequest)
    {
        return new ActionRequestDetails(
            actionRequest.Id,
            actionRequest.PublicId,
            actionRequest.Type,
            actionRequest.Status,
            actionRequest.EntityType,
            actionRequest.EntityId,
            actionRequest.Risk,
            actionRequest.Reason,
            actionRequest.PayloadJson,
            actionRequest.CreatedByChatId,
            actionRequest.ApprovedByChatId,
            actionRequest.CancelledByChatId,
            actionRequest.CreatedAt,
            actionRequest.ApprovedAt,
            actionRequest.CancelledAt,
            actionRequest.ExecutedAt,
            actionRequest.FailureReason);
    }

    private sealed record CustomerMessageActionPayload(
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("order_id")] string OrderId,
        [property: JsonPropertyName("order_number")] string? OrderNumber,
        [property: JsonPropertyName("findings")] IReadOnlyList<string> Findings,
        [property: JsonPropertyName("warning")] string Warning);
}
