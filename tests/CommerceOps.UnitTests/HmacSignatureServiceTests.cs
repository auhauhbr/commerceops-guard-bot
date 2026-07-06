using CommerceOps.Application.Security;

namespace CommerceOps.UnitTests;

public sealed class HmacSignatureServiceTests
{
    private readonly HmacSignatureService _service = new();

    [Fact]
    public void ComputeSignatureUsesTimestampDotRawBodyPayload()
    {
        const string secret = "test-secret";
        const string timestamp = "2026-07-06T12:00:00Z";
        const string rawBody = "{\"app_id\":\"lumora\",\"event_type\":\"payment_approved\"}";

        var signature = _service.ComputeSignature(secret, timestamp, rawBody);

        Assert.Equal("99c4e044e143aa8409421dc1cacde968fb070be8d8f1eb3d2c58a844f49f88ba", signature);
    }

    [Fact]
    public void IsValidSignatureAcceptsExpectedHexSignature()
    {
        const string secret = "test-secret";
        const string timestamp = "2026-07-06T12:00:00Z";
        const string rawBody = "{\"app_id\":\"lumora\"}";
        var signature = _service.ComputeSignature(secret, timestamp, rawBody);

        var isValid = _service.IsValidSignature(secret, timestamp, rawBody, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void IsValidSignatureAcceptsSha256Prefix()
    {
        const string secret = "test-secret";
        const string timestamp = "2026-07-06T12:00:00Z";
        const string rawBody = "{\"app_id\":\"lumora\"}";
        var signature = $"sha256={_service.ComputeSignature(secret, timestamp, rawBody)}";

        var isValid = _service.IsValidSignature(secret, timestamp, rawBody, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void IsValidSignatureRejectsDifferentRawBody()
    {
        const string secret = "test-secret";
        const string timestamp = "2026-07-06T12:00:00Z";
        const string rawBody = "{\"app_id\":\"lumora\"}";
        var signature = _service.ComputeSignature(secret, timestamp, rawBody);

        var isValid = _service.IsValidSignature(secret, timestamp, "{\"app_id\":\"other\"}", signature);

        Assert.False(isValid);
    }
}
