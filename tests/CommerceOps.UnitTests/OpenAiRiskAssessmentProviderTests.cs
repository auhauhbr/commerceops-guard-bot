using System.Net;
using System.Text;
using System.Text.Json;
using CommerceOps.Application.Triage;
using CommerceOps.Infrastructure.Risk;
using Microsoft.Extensions.Options;

namespace CommerceOps.UnitTests;

public sealed class OpenAiRiskAssessmentProviderTests
{
    [Fact]
    public async Task BuildsResponsesApiRequestWithSanitizedDtoAndStructuredOutputSchema()
    {
        var handler = new RecordingHandler(ValidApiResponse());
        var provider = CreateProvider(handler);

        await provider.AssessAsync(Request());

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/responses", handler.Uri?.AbsolutePath);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-only", handler.AuthorizationParameter);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.1, body.RootElement.GetProperty("temperature").GetDouble());
        var format = body.RootElement.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());

        using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
        var names = input.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(
            ["order_id", "order_status", "payment_status", "total_value", "item_count", "has_negative_stock", "findings", "derived_signals"],
            names);
    }

    [Fact]
    public async Task ParsesValidStructuredOutput()
    {
        var provider = CreateProvider(new RecordingHandler(ValidApiResponse()));

        var json = await provider.AssessAsync(Request());
        var result = JsonSerializer.Deserialize<AiRiskAssessmentResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(result);
        Assert.Equal("high", result.RiskLevel);
        Assert.Equal(82, result.RiskScore);
        Assert.Equal(0.91, result.Confidence);
    }

    [Fact]
    public async Task MissingApiKeyReturnsControlledErrorWithoutSendingRequest()
    {
        var handler = new RecordingHandler(ValidApiResponse());
        var provider = CreateProvider(handler, apiKey: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.AssessAsync(Request()));

        Assert.Contains("OPENAI_API_KEY", exception.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Http500ReturnsControlledErrorWithoutResponseBody()
    {
        var provider = CreateProvider(new RecordingHandler("sensitive upstream response", HttpStatusCode.InternalServerError));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.AssessAsync(Request()));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.DoesNotContain("sensitive", exception.Message);
    }

    [Fact]
    public async Task InvalidJsonReturnsControlledError()
    {
        var provider = CreateProvider(new RecordingHandler("not-json"));

        await Assert.ThrowsAnyAsync<JsonException>(() => provider.AssessAsync(Request()));
    }

    private static OpenAiRiskAssessmentProvider CreateProvider(RecordingHandler handler, string? apiKey = "test-only")
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        return new OpenAiRiskAssessmentProvider(client, Options.Create(new AiRiskOptions
        {
            Enabled = true,
            Provider = "openai",
            Model = "test-model",
            ApiKey = apiKey,
            TimeoutSeconds = 3
        }));
    }

    private static AiRiskAssessmentRequest Request() => new(
        "order-42", "pending", "approved", 799.90m, 2, true,
        ["negative_stock"],
        new Dictionary<string, bool> { ["is_payment_approved"] = true, ["is_order_pending"] = true });

    private static string ValidApiResponse()
    {
        var result = """
            {"risk_level":"high","risk_score":82,"primary_finding_code":"negative_stock","summary":"Revisar pedido","reasoning_summary":"Estoque negativo","recommended_action":"Revisão manual","customer_message_subject":"Atualização","customer_message_body":"Pedido em revisão.","confidence":0.91}
            """;
        return JsonSerializer.Serialize(new
        {
            id = "resp_test",
            output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = result } } } }
        });
    }

    private sealed class RecordingHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
