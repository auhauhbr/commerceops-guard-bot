using CommerceOps.Application.Actions;
using CommerceOps.Application.Lumora;
using CommerceOps.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CommerceOps.IntegrationTests;

public sealed class ActionRequestServiceTests : IClassFixture<CommerceOpsApiFactory>
{
    private readonly CommerceOpsApiFactory _factory;

    public ActionRequestServiceTests(CommerceOpsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ActionRequestServicePersistsListsAndApprovesActionRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
        dbContext.ActionRequests.RemoveRange(dbContext.ActionRequests);
        await dbContext.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IActionRequestService>();
        var draft = new CustomerMessageDraft(
            "1",
            NotificationChannel.Email,
            "Atualização sobre seu pedido #1",
            "Olá! Identificamos que seu pedido #1 está aguardando confirmação de pagamento.",
            "pending_order_without_approved_payment",
            "low",
            ["pending_order_without_approved_payment"],
            "Rascunho gerado. Nenhuma mensagem foi enviada.");

        var created = await service.CreateCustomerMessageEmailAsync(
            new CreateCustomerMessageActionRequest(draft, "1", "1", 123));

        var pending = await service.ListPendingAsync(10);
        var approved = await service.ApproveAsync(created.PublicId, 456);

        Assert.Equal("ACT-00001", created.PublicId);
        Assert.Equal(ActionRequestTypes.CustomerMessageEmail, created.Type);
        Assert.Equal(ActionRequestStatuses.PendingApproval, created.Status);
        Assert.Equal("order", created.EntityType);
        Assert.Equal("1", created.EntityId);
        Assert.Contains("\"warning\":\"draft_only\"", created.PayloadJson);
        Assert.DoesNotContain("signature", created.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", created.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Single(pending);
        Assert.Equal(ActionRequestStatuses.Approved, approved?.Status);
        Assert.Equal(456, approved?.ApprovedByChatId);
        Assert.NotNull(approved?.ApprovedAt);
    }
}
