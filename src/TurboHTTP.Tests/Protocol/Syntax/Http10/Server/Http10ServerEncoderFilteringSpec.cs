using System.Text;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Tests.Shared;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerEncoderFilteringSpec
{
    private static Http10ServerEncoder MakeEncoder(bool withDate = false) =>
        new(Http10ServerEncoderOptions.Default with { WriteDateHeader = withDate });

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    [InlineData("Connection")]
    [InlineData("Keep-Alive")]
    [InlineData("Transfer-Encoding")]
    [InlineData("TE")]
    [InlineData("Upgrade")]
    [InlineData("Proxy-Authenticate")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Trailer")]
    public void EncodeDeferred_should_strip_hop_by_hop_header(string headerName)
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers[headerName] = "some-value";

        var buf = new byte[512];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        // Verify hop-by-hop header does NOT appear in output
        Assert.DoesNotContain($"{headerName}:", wireOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.1")]
    public void EncodeDeferred_should_not_duplicate_existing_date_header()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["Date"] = DateTimeOffset.UtcNow.ToString("R");

        var buf = new byte[512];
        var written = MakeEncoder(withDate: true).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        // Count occurrences of "Date:" header
        var dateHeaderCount = 0;
        var pos = 0;
        while ((pos = wireOutput.IndexOf("Date:", pos, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            dateHeaderCount++;
            pos += 5; // Move past "Date:"
        }

        // Should appear exactly once
        Assert.Equal(1, dateHeaderCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void EncodeDeferred_should_emit_content_length_zero_for_empty_body()
    {
        var ctx = ServerTestContext.CreateResponse();

        var buf = new byte[256];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        // Wire output must contain Content-Length: 0
        Assert.Contains("Content-Length: 0", wireOutput);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void EncodeDeferred_should_strip_hop_by_hop_from_content_headers()
    {
        var ctx = ServerTestContext.CreateResponse();
        var hopByHopHeaders = new[]
        {
            "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Upgrade", "Proxy-Authenticate",
            "Proxy-Authorization", "Trailer"
        };

        foreach (var headerName in hopByHopHeaders)
        {
            ctx.Get<IHttpResponseFeature>()?.Headers[headerName] = "some-value";
        }

        var buf = new byte[512];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        // Verify ALL hop-by-hop headers are NOT in output
        foreach (var headerName in hopByHopHeaders)
        {
            Assert.DoesNotContain($"{headerName}:", wireOutput, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    [InlineData(200)]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(500)]
    public void EncodeDeferred_should_encode_valid_status_codes(int statusCode)
    {
        var ctx = ServerTestContext.CreateResponse(statusCode);

        var buf = new byte[4096];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        Assert.StartsWith($"HTTP/1.0 {statusCode}", wireOutput);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void EncodeDeferred_should_handle_status_with_empty_reason_phrase()
    {
        var ctx = ServerTestContext.CreateResponse();

        var buf = new byte[4096];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        Assert.StartsWith("HTTP/1.0 200", wireOutput);
    }
}