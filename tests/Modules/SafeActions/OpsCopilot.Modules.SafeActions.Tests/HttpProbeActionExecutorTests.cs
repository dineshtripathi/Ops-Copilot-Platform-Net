using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using OpsCopilot.SafeActions.Infrastructure.Validators;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="HttpProbeActionExecutor"/>.
/// Uses a fake <see cref="HttpMessageHandler"/> so no real network I/O occurs.
/// </summary>
public class HttpProbeActionExecutorTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Creates the SUT with a fake HTTP handler and optional config overrides.</summary>
    private static HttpProbeActionExecutor CreateSut(
        HttpMessageHandler handler,
        TargetUriValidator? validator = null,
        int timeoutMs = 5000,
        int maxResponseBytes = 1024)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SafeActions:HttpProbeTimeoutMs"] = timeoutMs.ToString(),
                ["SafeActions:HttpProbeMaxResponseBytes"] = maxResponseBytes.ToString(),
            })
            .Build();

        return new HttpProbeActionExecutor(
            new HttpClient(handler),
            validator ?? new TargetUriValidator(),
            config,
            NullLogger<HttpProbeActionExecutor>.Instance);
    }

    /// <summary>Creates valid JSON payload with the given url/method.</summary>
    private static string Payload(string? url = "https://example.com",
                                   string? method = null,
                                   int? timeoutMs = null)
    {
        var parts = new Dictionary<string, object?>();
        if (url is not null) parts["url"] = url;
        if (method is not null) parts["method"] = method;
        if (timeoutMs is not null) parts["timeoutMs"] = timeoutMs;
        return JsonSerializer.Serialize(parts);
    }

    // ── Execute: success path ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Success_For_200_Response()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "hello world");
        var sut = CreateSut(handler);

        var result = await sut.ExecuteAsync(Payload(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DurationMs >= 0);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("real_http_probe", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal(200, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("https://example.com", json.RootElement.GetProperty("url").GetString());
        Assert.False(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("hello world", json.RootElement.GetProperty("responseSnippet").GetString());
    }

    // ── Execute: non-success status code still returns result ───────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_For_500_Response()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "error");
        var sut = CreateSut(handler);

        var result = await sut.ExecuteAsync(Payload(), CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal(500, json.RootElement.GetProperty("statusCode").GetInt32());
    }

    // ── Execute: truncation ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Truncates_Response_Exceeding_MaxBytes()
    {
        var longBody = new string('A', 2000);
        var handler = new FakeHandler(HttpStatusCode.OK, longBody);
        var sut = CreateSut(handler, maxResponseBytes: 50);

        var result = await sut.ExecuteAsync(Payload(), CancellationToken.None);

        Assert.True(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(json.RootElement.GetProperty("responseSnippet").GetString()!.Length <= 50);
    }

    // ── Execute: invalid JSON payload ───────────────────────────────

    [Theory]
    [InlineData("not-json")]
    [InlineData("{broken")]
    public async Task ExecuteAsync_Returns_Failure_For_Invalid_Json(string payload)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "ok");
        var sut = CreateSut(handler);

        var result = await sut.ExecuteAsync(payload, CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("invalid_json", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Execute: method not GET ─────────────────────────────────────

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    public async Task ExecuteAsync_Rejects_NonGet_Method(string method)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "ok");
        var sut = CreateSut(handler);

        var result = await sut.ExecuteAsync(
            Payload(method: method), CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("method_not_allowed", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Execute: URL blocked by validator ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_UrlBlocked_When_Validator_Rejects()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "ok");
        var sut = CreateSut(handler, validator: new TargetUriValidator());

        // localhost is blocked by the real validator
        var result = await sut.ExecuteAsync(
            Payload(url: "https://localhost"), CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("url_blocked", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Execute: timeout ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Timeout_When_Request_Exceeds_Deadline()
    {
        var handler = new SlowHandler(delay: TimeSpan.FromSeconds(10));
        var sut = CreateSut(handler, timeoutMs: 50);

        var result = await sut.ExecuteAsync(Payload(), CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("timeout", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Execute: HTTP request exception ─────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_HttpError_On_RequestException()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection refused"));
        var sut = CreateSut(handler);

        var result = await sut.ExecuteAsync(Payload(), CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("http_error", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Execute: missing url field ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_UrlBlocked_When_Url_Missing()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "ok");
        var sut = CreateSut(handler, validator: new TargetUriValidator());

        var result = await sut.ExecuteAsync("{\"method\":\"GET\"}", CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("url_blocked", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Rollback: always fails ──────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_Failure()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "ok");
        var sut = CreateSut(handler);

        var result = await sut.RollbackAsync("{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.DurationMs);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Contains("not supported", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Execute: default method is GET ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Defaults_To_Get_When_Method_Absent()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "ok");
        var sut = CreateSut(handler);

        // payload with url but no method
        var result = await sut.ExecuteAsync(
            "{\"url\":\"https://example.com\"}", CancellationToken.None);

        Assert.True(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal(200, json.RootElement.GetProperty("statusCode").GetInt32());
    }

    // ── Execute: per-request timeout override ───────────────────────

    [Fact]
    public async Task ExecuteAsync_Uses_PerRequest_Timeout_Override()
    {
        var handler = new SlowHandler(delay: TimeSpan.FromSeconds(10));
        // Global timeout is high (30s) but per-request is 50ms → should timeout
        var sut = CreateSut(handler, timeoutMs: 30_000);

        var result = await sut.ExecuteAsync(
            Payload(timeoutMs: 50), CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("timeout", json.RootElement.GetProperty("reason").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test doubles
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Returns a fixed HTTP response.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body)
            });
        }
    }

    /// <summary>Delays for a long time, allowing timeout tests.</summary>
    private sealed class SlowHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        public SlowHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("slow response")
            };
        }
    }

    /// <summary>Always throws the given exception.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }
}
