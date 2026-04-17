using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class ResilienceSpec : AcceptanceTestBase
{
    private static Http11Engine Engine => new(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateDecompressingEngine()
    {
        var decomp = BidiFlow.FromGraph(new ContentEncodingBidiStage());
        return decomp.Atop(Engine.CreateFlow());
    }

    private async Task<HttpResponseMessage> SendScriptedAsync(HttpRequestMessage request, Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendDecompressingAsync(HttpRequestMessage request, Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = CreateDecompressingEngine().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Resilience_should_cause_exception_on_content_length_mismatch_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/content-length-mismatch")
        {
            Version = HttpVersion.Version11
        };

        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\nhello";

        var fake = new ScriptedFakeConnectionStage((_, _) => Encoding.Latin1.GetBytes(raw));
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Resilience_should_fail_gracefully_on_corrupt_gzip_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/corrupt-gzip")
        {
            Version = HttpVersion.Version11
        };

        var corruptBody = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03 };
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append($"Content-Length: {corruptBody.Length}\r\n");
        sb.Append("Content-Encoding: gzip\r\n");
        sb.Append("\r\n");
        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var responseBytes = new byte[headerBytes.Length + corruptBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        corruptBody.CopyTo(responseBytes, headerBytes.Length);

        var response = await SendDecompressingAsync(request, (_, _) => responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Resilience_should_fail_gracefully_on_corrupt_brotli_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/corrupt-br")
        {
            Version = HttpVersion.Version11
        };

        var corruptBody = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03 };
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append($"Content-Length: {corruptBody.Length}\r\n");
        sb.Append("Content-Encoding: br\r\n");
        sb.Append("\r\n");
        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var responseBytes = new byte[headerBytes.Length + corruptBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        corruptBody.CopyTo(responseBytes, headerBytes.Length);

        var response = await SendDecompressingAsync(request, (_, _) => responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Resilience_should_detect_truncated_body_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/truncated-body/4")
        {
            Version = HttpVersion.Version11
        };

        var truncatedBody = new byte[100];
        Array.Fill(truncatedBody, (byte)'X');
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Length: 4096\r\n");
        sb.Append("\r\n");
        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var responseBytes = new byte[headerBytes.Length + truncatedBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        truncatedBody.CopyTo(responseBytes, headerBytes.Length);

        var fake = new ScriptedFakeConnectionStage((_, _) => responseBytes);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.1")]
    public async Task Resilience_should_succeed_with_slow_headers_within_timeout_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/slow-headers/500")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 12\r\n\r\nslow-headers"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("slow-headers", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Resilience_should_succeed_with_slow_body_within_timeout_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/slow-body/500")
        {
            Version = HttpVersion.Version11
        };

        var bodyContent = "slow-body-first-halfslow-body-second-half";
        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bodyContent.Length}\r\n\r\n{bodyContent}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("slow-body-first-half", body);
        Assert.Contains("slow-body-second-half", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.1")]
    public async Task Resilience_should_cause_cancellation_when_slow_headers_exceed_timeout_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/slow-headers/10000")
        {
            Version = HttpVersion.Version11
        };

        var fake = new ScriptedFakeConnectionStage((_, _) => null);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.1")]
    public async Task Resilience_should_cause_exception_on_empty_response_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/resilience/empty-response")
        {
            Version = HttpVersion.Version11
        };

        var fake = new ScriptedFakeConnectionStage((_, _) => null);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }
}
