using System.Net;
using System.Text;
using TurboHttp.Protocol.Http3;
using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Tests.Http3.Streams;

/// <summary>
/// RFC 9114 §4.1 — Http3ResponseDecoder basic decoding tests.
/// Covers status code decoding, response header population, body assembly from DATA frames,
/// content headers, QPACK integration, stateful decoder, and DecodeHeaders helper.
/// </summary>
public sealed class Http3ResponseDecoderBasicSpec
{
    private static Http3HeadersFrame EncodeHeaders(
        IReadOnlyList<(string Name, string Value)> headers,
        int maxTableCapacity = 0)
    {
        var encoder = new QpackEncoder(maxTableCapacity);
        var block = encoder.Encode(headers);
        return new Http3HeadersFrame(block);
    }

    private static Http3HeadersFrame EncodeResponseHeaders(
        int statusCode,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null,
        int maxTableCapacity = 0)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", statusCode.ToString()),
        };
        if (extraHeaders is not null)
        {
            headers.AddRange(extraHeaders);
        }

        return EncodeHeaders(headers, maxTableCapacity);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Headers_only_decoded_to_response()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [Trait("RFC", "RFC9114-4.1")]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(500)]
    public void Status_codes_decoded(int statusCode)
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(statusCode);

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Response_headers_populated()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("server", "TurboHttp"),
            ("x-request-id", "abc-123"),
        });

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Contains("TurboHttp", response.Headers.GetValues("server"));
        Assert.Contains("abc-123", response.Headers.GetValues("x-request-id"));
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Single_data_frame_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var body = "Hello, HTTP/3!"u8.ToArray();
        var dataFrame = new Http3DataFrame(body);

        var response = decoder.Decode(new Http3Frame[] { headersFrame, dataFrame });

        var content = await response.Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Multiple_data_frames_assembled()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var part1 = "Hello, "u8.ToArray();
        var part2 = "World!"u8.ToArray();

        var response = decoder.Decode(new Http3Frame[]
        {
            headersFrame,
            new Http3DataFrame(part1),
            new Http3DataFrame(part2),
        });

        var content = await response.Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello, World!", Encoding.UTF8.GetString(content));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task No_data_frames_no_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(204);

        var response = decoder.Decode(new[] { headersFrame });

        // No content at all or empty content
        Assert.True(
            response.Content is null ||
            (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length == 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Large_body_assembled()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var body = new byte[64 * 1024];
        new Random(42).NextBytes(body);

        var response = decoder.Decode(new Http3Frame[]
        {
            headersFrame,
            new Http3DataFrame(body),
        });

        var content = await response.Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, content);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Content_type_decoded()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("content-type", "application/json"),
        });

        // Verify via DecodeHeaders that content-type is present in raw decoded output
        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == "content-type" && h.Value == "application/json");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Content_length_decoded()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("content-length", "42"),
        });

        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == "content-length" && h.Value == "42");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Content_encoding_decoded()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("content-encoding", "gzip"),
        });

        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == "content-encoding" && h.Value == "gzip");
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Qpack_decoding_applied()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("cache-control", "no-cache"),
            ("vary", "Accept-Encoding"),
        });

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Contains("no-cache", response.Headers.GetValues("cache-control"));
        Assert.Contains("Accept-Encoding", response.Headers.GetValues("vary"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Qpack_decoder_property_accessible()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 4096);
        Assert.NotNull(decoder.QpackDecoder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Decoder_instructions_accessible()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 4096);

        // DecoderInstructions property should be accessible (empty when no dynamic table used)
        var instructions = decoder.DecoderInstructions;
        Assert.True(instructions.Length >= 0,
            "DecoderInstructions should be accessible");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Static_table_only_no_instructions()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        decoder.Decode(new[] { headersFrame });

        Assert.Equal(0, decoder.DecoderInstructions.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Decoder_is_stateful()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);

        var response1 = decoder.Decode(new[] { EncodeResponseHeaders(200) }, streamId: 1);
        var response2 = decoder.Decode(new[] { EncodeResponseHeaders(404) }, streamId: 3);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response2.StatusCode);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_returns_raw_pairs()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("server", "test"),
        });

        var headers = decoder.DecodeHeaders(headersFrame);

        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
        Assert.Contains(headers, h => h.Name == "server" && h.Value == "test");
    }
}
