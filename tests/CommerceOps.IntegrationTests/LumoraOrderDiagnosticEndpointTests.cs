using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommerceOps.Application.Lumora;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommerceOps.IntegrationTests;

public sealed class LumoraOrderDiagnosticEndpointTests : IClassFixture<CommerceOpsApiFactory>
{
    private const string Secret = "lumora-diagnostic-secret";

    private readonly CommerceOpsApiFactory _factory;

    public LumoraOrderDiagnosticEndpointTests(CommerceOpsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOrderDiagnosticReturnsLumoraDiagnostic()
    {
        using var client = CreateClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(
            new LumoraOrderDiagnosticResponse(
                "1",
                "pending_payment",
                "pending",
                "ok",
                [
                    new LumoraDiagnosticFinding(
                        "pending_order_without_approved_payment",
                        "info",
                        "Order is pending and does not have an approved payment.",
                        JsonDocument.Parse("""
                        {
                          "code": "pending_order_without_approved_payment",
                          "details": {
                            "order_status": "pending_payment",
                            "payment_status": "pending"
                          }
                        }
                        """).RootElement.Clone())
                ],
                "1 operational finding(s) detected.",
                "low",
                "1",
                "899.00",
                "899.00",
                "0.00",
                DateTimeOffset.Parse("2026-07-07T01:55:47Z"),
                DateTimeOffset.Parse("2026-07-07T01:55:48Z"),
                [
                    new LumoraOrderDiagnosticItem(
                        "1",
                        "1",
                        "Ponto De Acesso Ubiquiti UniFi U6+ Wi-Fi 6 Interno",
                        "899.00",
                        1,
                        "899.00",
                        true,
                        8)
                ])));

        var response = await client.GetAsync("/api/integrations/lumora/orders/1/diagnostic");
        var body = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", payload.RootElement.GetProperty("order_id").GetString());
        Assert.Equal("pending_payment", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("pending", payload.RootElement.GetProperty("payment_status").GetString());
        Assert.Equal("ok", payload.RootElement.GetProperty("stock_status").GetString());
        Assert.Equal("1 operational finding(s) detected.", payload.RootElement.GetProperty("summary").GetString());
        Assert.Equal("low", payload.RootElement.GetProperty("risk").GetString());
        Assert.Equal("1", payload.RootElement.GetProperty("order_number").GetString());
        Assert.Equal("899.00", payload.RootElement.GetProperty("total").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("findings").GetArrayLength());
        Assert.Equal(1, payload.RootElement.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrderDiagnosticReturnsSafeNotFoundWhenLumoraDoesNotFindOrder()
    {
        using var client = CreateClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure(
            "not_found",
            $"Order not found. {Secret}",
            404));

        var response = await client.GetAsync("/api/integrations/lumora/orders/999/diagnostic");
        var body = await response.Content.ReadAsStringAsync();
        var payload = await response.Content.ReadFromJsonAsync<LumoraDiagnosticErrorResponse>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", payload?.Status);
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrderDiagnosticReturnsSafeUnavailableWhenLumoraFails()
    {
        using var client = CreateClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure(
            "unavailable",
            $"Nao foi possivel conectar com a Lumora. {Secret}"));

        var response = await client.GetAsync("/api/integrations/lumora/orders/1/diagnostic");
        var body = await response.Content.ReadAsStringAsync();
        var payload = await response.Content.ReadFromJsonAsync<LumoraDiagnosticUnavailableResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("lumora", payload?.Integration);
        Assert.Equal("unavailable", payload?.Status);
        Assert.Equal("unavailable", payload?.ErrorCode);
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(LumoraClientResult<LumoraOrderDiagnosticResponse> result)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILumoraClient>();
                services.AddSingleton<ILumoraClient>(new FakeLumoraClient(result));
            });
        }).CreateClient();
    }

    private sealed class FakeLumoraClient : ILumoraClient
    {
        private readonly LumoraClientResult<LumoraOrderDiagnosticResponse> _diagnosticResult;

        public FakeLumoraClient(LumoraClientResult<LumoraOrderDiagnosticResponse> diagnosticResult)
        {
            _diagnosticResult = diagnosticResult;
        }

        public Task<LumoraClientResult<LumoraHealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraOrderDiagnosticResponse>> GetOrderDiagnosticAsync(
            string orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_diagnosticResult);

        public Task<LumoraClientResult<LumoraOrderTriageCandidatesResponse>> GetTriageCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraPaymentInconsistenciesResponse>> GetPaymentInconsistenciesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraInventoryInconsistenciesResponse>> GetInventoryInconsistenciesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraDatabaseIntegrityResponse>> GetDatabaseIntegrityAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraSlowQueriesResponse>> GetSlowQueriesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraFailedJobsResponse>> GetFailedJobsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record LumoraDiagnosticErrorResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("message")] string Message);

    private sealed record LumoraDiagnosticUnavailableResponse(
        [property: JsonPropertyName("integration")] string Integration,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("error_code")] string ErrorCode,
        [property: JsonPropertyName("message")] string Message);
}
