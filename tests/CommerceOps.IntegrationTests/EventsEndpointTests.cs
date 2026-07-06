using System.Net;
using System.Text;
using System.Text.Json;
using CommerceOps.Application.Security;
using CommerceOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CommerceOps.IntegrationTests;

public sealed class EventsEndpointTests : IClassFixture<CommerceOpsApiFactory>
{
    private readonly CommerceOpsApiFactory _factory;
    private readonly HttpClient _client;
    private readonly HmacSignatureService _signatureService = new();

    public EventsEndpointTests(CommerceOpsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
        dbContext.CaseFindings.RemoveRange(dbContext.CaseFindings);
        dbContext.OperationalCases.RemoveRange(dbContext.OperationalCases);
        dbContext.OperationalEvents.RemoveRange(dbContext.OperationalEvents);
        dbContext.SaveChanges();
    }

    [Fact]
    public async Task PostEventWithValidSignaturePersistsEvent()
    {
        var rawBody = CreateValidPayload();
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        using var request = CreateSignedRequest(rawBody, timestamp, CommerceOpsApiFactory.AppSecret);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
        var storedEvent = await dbContext.OperationalEvents.SingleAsync();

        Assert.Equal("payment_approved", storedEvent.EventType);
        Assert.Equal(rawBody, storedEvent.RawBody);
        Assert.Equal("{\"order_id\":1042,\"payment_status\":\"approved\"}", storedEvent.DataJson);
    }

    [Fact]
    public async Task PostPaymentApprovedWithPendingOrderCreatesCase()
    {
        var rawBody = CreatePayload(
            eventType: "payment_approved",
            entityType: "order",
            entityId: "1042",
            dataJson: """{"order_id":1042,"payment_status":"approved","order_status":"pending"}""");
        using var request = CreateSignedRequest(rawBody, DateTimeOffset.UtcNow.ToString("O"), CommerceOpsApiFactory.AppSecret);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
        var operationalCase = await dbContext.OperationalCases.Include(currentCase => currentCase.Findings).SingleAsync();

        Assert.Equal("CASE-00001", operationalCase.CaseNumber);
        Assert.Equal("Pedido pago não confirmado", operationalCase.Title);
        Assert.Equal("medium", operationalCase.RiskLevel);
        Assert.Equal(40, operationalCase.RiskScore);
        Assert.Single(operationalCase.Findings);
    }

    [Fact]
    public async Task PostInventoryNegativeCreatesCase()
    {
        var rawBody = CreatePayload(
            eventType: "inventory_negative",
            entityType: "product",
            entityId: "SKU-001",
            dataJson: """{"sku":"SKU-001","stock":-1}""");
        using var request = CreateSignedRequest(rawBody, DateTimeOffset.UtcNow.ToString("O"), CommerceOpsApiFactory.AppSecret);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
        var operationalCase = await dbContext.OperationalCases.SingleAsync();

        Assert.Equal("Estoque negativo", operationalCase.Title);
        Assert.Equal("medium", operationalCase.RiskLevel);
        Assert.Equal(35, operationalCase.RiskScore);
    }

    [Fact]
    public async Task PostNonCriticalEventDoesNotCreateCase()
    {
        var rawBody = CreatePayload(
            eventType: "order_created",
            entityType: "order",
            entityId: "1042",
            dataJson: """{"order_status":"pending"}""");
        using var request = CreateSignedRequest(rawBody, DateTimeOffset.UtcNow.ToString("O"), CommerceOpsApiFactory.AppSecret);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
        Assert.Empty(dbContext.OperationalCases);
    }

    [Fact]
    public async Task PostDuplicateCriticalEventDoesNotCreateDuplicateOpenCase()
    {
        var rawBody = CreatePayload(
            eventType: "inventory_negative",
            entityType: "product",
            entityId: "SKU-001",
            dataJson: """{"sku":"SKU-001","stock":-1}""");

        using var firstRequest = CreateSignedRequest(rawBody, DateTimeOffset.UtcNow.ToString("O"), CommerceOpsApiFactory.AppSecret);
        using var secondRequest = CreateSignedRequest(rawBody, DateTimeOffset.UtcNow.ToString("O"), CommerceOpsApiFactory.AppSecret);

        var firstResponse = await _client.SendAsync(firstRequest);
        var secondResponse = await _client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
        Assert.Equal(2, await dbContext.OperationalEvents.CountAsync());
        Assert.Single(dbContext.OperationalCases);
    }

    [Fact]
    public async Task GetCasesListsCases()
    {
        var rawBody = CreatePayload(
            eventType: "inventory_negative",
            entityType: "product",
            entityId: "SKU-001",
            dataJson: """{"sku":"SKU-001","stock":-1}""");
        using var request = CreateSignedRequest(rawBody, DateTimeOffset.UtcNow.ToString("O"), CommerceOpsApiFactory.AppSecret);
        await _client.SendAsync(request);

        var response = await _client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cases = document.RootElement;
        Assert.Equal(JsonValueKind.Array, cases.ValueKind);
        Assert.Single(cases.EnumerateArray());
        Assert.Equal("Estoque negativo", cases[0].GetProperty("title").GetString());
        Assert.Equal("CASE-00001", cases[0].GetProperty("case_number").GetString());
    }

    [Fact]
    public async Task GetCaseByIdReturnsDetails()
    {
        var rawBody = CreatePayload(
            eventType: "payment_approved",
            entityType: "order",
            entityId: "1042",
            dataJson: """{"order_id":1042,"payment_status":"approved","order_status":"pending"}""");
        using var request = CreateSignedRequest(rawBody, DateTimeOffset.UtcNow.ToString("O"), CommerceOpsApiFactory.AppSecret);
        await _client.SendAsync(request);

        Guid caseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
            caseId = await dbContext.OperationalCases.Select(operationalCase => operationalCase.Id).SingleAsync();
        }

        var response = await _client.GetAsync($"/api/cases/{caseId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(caseId, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Pedido pago não confirmado", document.RootElement.GetProperty("title").GetString());
        Assert.Single(document.RootElement.GetProperty("findings").EnumerateArray());
    }

    [Fact]
    public async Task PostEventWithoutSignatureIsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/events")
        {
            Content = new StringContent(CreateValidPayload(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CommerceOps-App", CommerceOpsApiFactory.AppPublicId);
        request.Headers.Add("X-CommerceOps-Timestamp", DateTimeOffset.UtcNow.ToString("O"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostEventWithInvalidSignatureIsRejected()
    {
        using var request = CreateSignedRequest(CreateValidPayload(), DateTimeOffset.UtcNow.ToString("O"), "wrong-secret");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostEventWithOldTimestampIsRejected()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O");
        using var request = CreateSignedRequest(CreateValidPayload(), timestamp, CommerceOpsApiFactory.AppSecret);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpRequestMessage CreateSignedRequest(string rawBody, string timestamp, string secret)
    {
        var signature = _signatureService.ComputeSignature(secret, timestamp, rawBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/events")
        {
            Content = new StringContent(rawBody, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("X-CommerceOps-App", CommerceOpsApiFactory.AppPublicId);
        request.Headers.Add("X-CommerceOps-Timestamp", timestamp);
        request.Headers.Add("X-CommerceOps-Signature", signature);

        return request;
    }

    private static string CreateValidPayload()
    {
        return """
            {"app_id":"lumora","event_type":"payment_approved","entity_type":"order","entity_id":"1042","occurred_at":"2026-07-03T22:30:00Z","severity":"info","data":{"order_id":1042,"payment_status":"approved"}}
            """;
    }

    private static string CreatePayload(string eventType, string entityType, string entityId, string dataJson)
    {
        return $$"""
            {"app_id":"lumora","event_type":"{{eventType}}","entity_type":"{{entityType}}","entity_id":"{{entityId}}","occurred_at":"2026-07-03T22:30:00Z","severity":"info","data":{{dataJson}}}
            """;
    }
}
