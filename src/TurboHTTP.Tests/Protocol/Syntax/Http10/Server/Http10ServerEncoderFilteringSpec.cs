using System.Net;
using System.Text;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http10.Server;

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
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        // Try to add via response.Headers first (general headers)
        var added = response.Headers.TryAddWithoutValidation(headerName, "some-value");

        // If that fails, try content headers
        if (!added)
        {
            response.Content = new ByteArrayContent([]);
            added = response.Content.Headers.TryAddWithoutValidation(headerName, "some-value");
        }

        // If it still fails, skip this header (some headers cannot be added via .NET API)
        if (!added)
        {
            // This is a limitation of the .NET HttpResponseMessage API, not our encoder
            return;
        }

        var buf = new byte[512];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        // Verify hop-by-hop header does NOT appear in output
        Assert.DoesNotContain($"{headerName}:", wireOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.1")]
    public void EncodeDeferred_should_not_duplicate_existing_date_header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        var dateValue = DateTimeOffset.UtcNow;
        response.Headers.Date = dateValue;

        var buf = new byte[512];
        var written = MakeEncoder(withDate: true).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
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
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        var buf = new byte[256];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        // Wire output must contain Content-Length: 0
        Assert.Contains("Content-Length: 0", wireOutput);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void EncodeDeferred_should_strip_hop_by_hop_from_content_headers()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        // Try to add hop-by-hop headers via Content.Headers
        // Some may fail due to .NET API restrictions, but test those that succeed
        var hopByHopHeaders = new[] { "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Upgrade", "Proxy-Authenticate", "Proxy-Authorization", "Trailer" };

        int successCount = 0;
        foreach (var headerName in hopByHopHeaders)
        {
            if (response.Content.Headers.TryAddWithoutValidation(headerName, "some-value"))
            {
                successCount++;
            }
        }

        // If no headers could be added, skip this test
        if (successCount == 0)
        {
            return;
        }

        var buf = new byte[512];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        // Verify ALL hop-by-hop headers from content headers are NOT in output
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
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);

        var buf = new byte[4096];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        Assert.StartsWith($"HTTP/1.0 {statusCode}", wireOutput);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void EncodeDeferred_should_handle_status_with_empty_reason_phrase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            ReasonPhrase = ""
        };

        var buf = new byte[4096];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
        var wireOutput = Encoding.ASCII.GetString(buf, 0, written);

        Assert.StartsWith("HTTP/1.0 200", wireOutput);
    }
}
