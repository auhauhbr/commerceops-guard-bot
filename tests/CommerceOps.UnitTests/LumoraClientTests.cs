using System.Net;
using CommerceOps.Application.Lumora;
using CommerceOps.Infrastructure.Lumora;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommerceOps.UnitTests;

public sealed class LumoraClientTests
{
    [Fact]
    public async Task GetHealthSendsSignedHeadersAndParsesResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "status": "healthy",
              "checked_at": "2026-07-06T12:00:00Z",
              "database": { "status": "healthy" },
              "queue": { "status": "healthy" }
            }
            """)
        });
        var client = CreateClient(handler, logger: new CapturingLogger<LumoraClient>());

        var result = await client.GetHealthAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("healthy", result.Data?.Status);
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.Equal("/commerceops/health", handler.LastRequest?.RequestUri?.PathAndQuery);
        Assert.Equal("lumora", handler.LastRequest?.Headers.GetValues("X-CommerceOps-App").Single());
        Assert.True(handler.LastRequest?.Headers.Contains("X-CommerceOps-Timestamp"));
        Assert.True(handler.LastRequest?.Headers.Contains("X-CommerceOps-Signature"));
        Assert.DoesNotContain("super-secret", handler.LastRequest?.Headers.ToString());
    }

    [Fact]
    public async Task GetOrderDiagnosticEscapesOrderId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "order_id": "A/B 42",
              "status": "pending",
              "payment_status": "approved",
              "stock_status": "pending",
              "findings": []
            }
            """)
        });
        var client = CreateClient(handler, logger: new CapturingLogger<LumoraClient>());

        var result = await client.GetOrderDiagnosticAsync("A/B 42");

        Assert.True(result.IsSuccess);
        Assert.Equal("/commerceops/orders/A%2FB%2042/diagnostic", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task NonSuccessStatusReturnsFriendlyErrorWithoutLoggingSecret()
    {
        var logger = new CapturingLogger<LumoraClient>();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler, logger);

        var result = await client.GetHealthAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("http_error", result.Error?.Code);
        Assert.Equal(500, result.Error?.StatusCode);
        Assert.DoesNotContain("super-secret", logger.Messages);
        Assert.DoesNotContain("sha256=", logger.Messages);
    }

    [Fact]
    public async Task NotFoundStatusReturnsNotFoundError()
    {
        var logger = new CapturingLogger<LumoraClient>();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler, logger);

        var result = await client.GetOrderDiagnosticAsync("missing-order");

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error?.Code);
        Assert.Equal(404, result.Error?.StatusCode);
        Assert.DoesNotContain("super-secret", logger.Messages);
        Assert.DoesNotContain("sha256=", logger.Messages);
    }

    [Fact]
    public async Task InvalidJsonReturnsFriendlyError()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{invalid")
        });
        var client = CreateClient(handler, logger: new CapturingLogger<LumoraClient>());

        var result = await client.GetHealthAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_response", result.Error?.Code);
    }

    [Fact]
    public async Task TimeoutReturnsFriendlyError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new OperationCanceledException());
        var client = CreateClient(handler, logger: new CapturingLogger<LumoraClient>());

        var result = await client.GetHealthAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("timeout", result.Error?.Code);
    }

    [Fact]
    public async Task MissingConfigurationDoesNotSendRequest()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://lumora.test")
        };
        var client = new LumoraClient(
            httpClient,
            Options.Create(new LumoraOptions()),
            TimeProvider.System,
            new CapturingLogger<LumoraClient>());

        var result = await client.GetHealthAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("not_configured", result.Error?.Code);
        Assert.Null(handler.LastRequest);
    }

    private static LumoraClient CreateClient(FakeHttpMessageHandler handler, ILogger<LumoraClient> logger)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://lumora.test")
        };

        return new LumoraClient(
            httpClient,
            Options.Create(new LumoraOptions
            {
                AppId = "lumora",
                BaseUrl = "https://lumora.test",
                SharedSecret = "super-secret",
                TimeoutSeconds = 10
            }),
            TimeProvider.System,
            logger);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public string Messages => string.Join(Environment.NewLine, _messages);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                _messages.Add(exception.Message);
            }
        }
    }
}
