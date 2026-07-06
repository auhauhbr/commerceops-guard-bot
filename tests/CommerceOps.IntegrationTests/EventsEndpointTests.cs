using System.Net;
using System.Text;
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
}
