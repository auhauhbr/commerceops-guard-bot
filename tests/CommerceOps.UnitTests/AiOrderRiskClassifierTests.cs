using CommerceOps.Application.Triage;
using CommerceOps.Infrastructure.Risk;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommerceOps.UnitTests;

public sealed class AiOrderRiskClassifierTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-11T12:00:00Z");

    [Fact]
    public async Task DisabledAiUsesDeterministicWithoutCallingProvider()
    {
        var provider = new FakeProvider(ValidJson());
        var classifier = CreateClassifier(provider, enabled: false);

        var result = await classifier.ClassifyAsync(Candidate("disabled", ["order_total_mismatch"]), Now);

        Assert.Equal("deterministic", result.RiskSource);
        Assert.Equal(50, result.RiskScore);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ValidAiResponseIsAccepted()
    {
        var provider = new FakeProvider(ValidJson(score: 86, finding: "negative_stock"));
        var classifier = CreateClassifier(provider);

        var result = await classifier.ClassifyAsync(Candidate("valid", ["negative_stock"], itemCount: 3), Now);

        Assert.Equal("ai", result.RiskSource);
        Assert.Equal(86, result.RiskScore);
        Assert.Equal("negative_stock", result.PrimaryFindingCode);
        Assert.Equal(3, provider.Request?.ItemCount);
    }

    [Fact]
    public async Task InvalidJsonUsesFallback()
    {
        var result = await CreateClassifier(new FakeProvider("not-json"))
            .ClassifyAsync(Candidate("invalid-json", ["order_total_mismatch"]), Now);

        Assert.Equal("fallback", result.RiskSource);
        Assert.Equal(50, result.RiskScore);
    }

    [Fact]
    public async Task TimeoutUsesFallback()
    {
        var result = await CreateClassifier(new FakeProvider(delay: TimeSpan.FromSeconds(2)), timeoutSeconds: 1)
            .ClassifyAsync(Candidate("timeout", ["payment_missing"]), Now);

        Assert.Equal("fallback", result.RiskSource);
        Assert.Equal(30, result.RiskScore);
    }

    [Fact]
    public async Task ProviderErrorUsesFallback()
    {
        var result = await CreateClassifier(new FakeProvider(exception: new HttpRequestException("failed")))
            .ClassifyAsync(Candidate("error", ["order_total_mismatch"]), Now);

        Assert.Equal("fallback", result.RiskSource);
        Assert.Equal(50, result.RiskScore);
    }

    [Fact]
    public async Task UnknownPrimaryFindingUsesFallback()
    {
        var result = await CreateClassifier(new FakeProvider(ValidJson(finding: "delete_order")))
            .ClassifyAsync(Candidate("unknown-finding", ["payment_missing"]), Now);

        Assert.Equal("fallback", result.RiskSource);
        Assert.Equal("payment_missing", result.PrimaryFindingCode);
    }

    [Fact]
    public async Task GuardrailRejectsLowRiskForCriticalFinding()
    {
        var result = await CreateClassifier(new FakeProvider(ValidJson(score: 10, level: "low", finding: "negative_stock")))
            .ClassifyAsync(Candidate("critical-low", ["negative_stock"]), Now);

        Assert.Equal("fallback", result.RiskSource);
        Assert.Equal("high", result.RiskLevel);
        Assert.True(result.RiskScore >= 70);
    }

    private static AiOrderRiskClassifier CreateClassifier(
        IAiRiskAssessmentProvider provider,
        bool enabled = true,
        int timeoutSeconds = 3) =>
        new(
            provider,
            new DeterministicOrderRiskClassifier(new OrderRiskScorer()),
            new AiRiskAssessmentGuardrail(),
            Options.Create(new AiRiskOptions
            {
                Enabled = enabled,
                TimeoutSeconds = timeoutSeconds,
                Provider = "fake",
                Model = "fake-model",
                ApiKey = "test-only"
            }),
            NullLogger<AiOrderRiskClassifier>.Instance);

    private static OrderTriageCandidate Candidate(string id, IReadOnlyList<string> findings, int? itemCount = null) =>
        new(id, id, "pending", "pending", null, false, 100m, Now, findings, itemCount);

    private static string ValidJson(
        int score = 80,
        string? level = null,
        string finding = "payment_missing") => $$"""
        {
          "risk_level": "{{level ?? (score >= 70 ? "high" : score >= 30 ? "medium" : "low")}}",
          "risk_score": {{score}},
          "primary_finding_code": "{{finding}}",
          "summary": "Revisão operacional necessária",
          "reasoning_summary": "Sinais conhecidos indicam risco",
          "recommended_action": "Revisar manualmente o pedido",
          "customer_message_subject": "Atualização do pedido",
          "customer_message_body": "Seu pedido está em revisão.",
          "confidence": 0.9
        }
        """;

    private sealed class FakeProvider : IAiRiskAssessmentProvider
    {
        private readonly string? _json;
        private readonly TimeSpan _delay;
        private readonly Exception? _exception;

        public FakeProvider(string? json = null, TimeSpan delay = default, Exception? exception = null)
        {
            _json = json;
            _delay = delay;
            _exception = exception;
        }

        public int Calls { get; private set; }
        public AiRiskAssessmentRequest? Request { get; private set; }

        public async Task<string> AssessAsync(AiRiskAssessmentRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            if (_exception is not null)
            {
                throw _exception;
            }

            return _json ?? ValidJson();
        }
    }
}
