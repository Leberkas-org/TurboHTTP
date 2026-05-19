using System.Net;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Encoder;

public sealed class Http2ServerEncoderFragmentationSpec
{
    private readonly Http2ServerEncoder _encoder = new();
    private readonly HpackDecoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void EncodeHeaders_exceeding_MaxFrameSize_should_produce_CONTINUATION_frames()
    {
        // Arrange: Set MaxFrameSize to 64 bytes to force fragmentation
        _encoder.ApplyClientSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        // Create a response with headers large enough to exceed 64 bytes when encoded
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        // Add multiple headers to ensure the encoded block exceeds MaxFrameSize
        for (int i = 0; i < 10; i++)
        {
            response.Headers.Add($"x-header-{i}", $"this-is-a-long-header-value-to-force-fragmentation-{i}");
        }

        // Act
        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: false);

        // Assert
        Assert.True(frames.Count >= 2, "Expected at least 2 frames due to fragmentation");
        Assert.IsType<HeadersFrame>(frames[0]);
        for (int i = 1; i < frames.Count; i++)
        {
            Assert.IsType<ContinuationFrame>(frames[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void EncodeHeaders_CONTINUATION_frames_should_not_carry_EndStream()
    {
        // Arrange: Set MaxFrameSize to 64 bytes to force fragmentation
        _encoder.ApplyClientSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("body content"u8.ToArray()),
        };
        for (int i = 0; i < 10; i++)
        {
            response.Headers.Add($"x-header-{i}", $"this-is-a-long-header-value-to-force-fragmentation-{i}");
        }

        // Act
        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: true);

        // Assert
        Assert.True(frames.Count >= 2);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndStream, "HeadersFrame should not have EndStream when body follows");

        for (int i = 1; i < frames.Count; i++)
        {
            var continuationFrame = Assert.IsType<ContinuationFrame>(frames[i]);
            Assert.Equal(1, continuationFrame.StreamId);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void EncodeHeaders_only_last_CONTINUATION_has_EndHeaders()
    {
        // Arrange: Set MaxFrameSize to 64 bytes to force fragmentation
        _encoder.ApplyClientSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        for (int i = 0; i < 10; i++)
        {
            response.Headers.Add($"x-header-{i}", $"this-is-a-long-header-value-to-force-fragmentation-{i}");
        }

        // Act
        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: false);

        // Assert
        Assert.True(frames.Count >= 2);

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndHeaders, "HeadersFrame should not have EndHeaders when fragments follow");

        for (int i = 1; i < frames.Count - 1; i++)
        {
            var continuationFrame = Assert.IsType<ContinuationFrame>(frames[i]);
            Assert.False(continuationFrame.EndHeaders, $"Intermediate ContinuationFrame at index {i} should not have EndHeaders");
        }

        var lastFrame = Assert.IsType<ContinuationFrame>(frames[^1]);
        Assert.True(lastFrame.EndHeaders, "Only the last ContinuationFrame should have EndHeaders");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void ApplyClientSettings_should_update_MaxFrameSize()
    {
        // Arrange
        var defaultSize = _encoder.MaxFrameSize;
        Assert.Equal(16 * 1024, defaultSize);

        // Act
        _encoder.ApplyClientSettings([(SettingsParameter.MaxFrameSize, 32768u)]);

        // Assert
        Assert.Equal(32768, _encoder.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.2")]
    public void EncodeHeaders_fragmented_headers_should_decode_correctly()
    {
        // Arrange: Set MaxFrameSize to 64 bytes to force fragmentation
        _encoder.ApplyClientSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Headers.Add("x-custom-header", "custom-value");
        response.Headers.Add("x-another-header", "another-value");
        for (int i = 0; i < 8; i++)
        {
            response.Headers.Add($"x-header-{i}", $"header-value-{i}");
        }

        // Act
        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: false);

        // Assert: All frames have the expected stream ID
        Assert.All(frames, f => Assert.Equal(1, f.StreamId));

        // Reassemble all header block fragments
        var headerBlockBytes = new List<byte>();
        foreach (var frame in frames)
        {
            if (frame is HeadersFrame hf)
            {
                headerBlockBytes.AddRange(hf.HeaderBlockFragment.Span.ToArray());
            }
            else if (frame is ContinuationFrame cf)
            {
                headerBlockBytes.AddRange(cf.HeaderBlockFragment.Span.ToArray());
            }
        }

        // Decode the reassembled header block
        var decodedHeaders = _decoder.Decode(headerBlockBytes.ToArray());

        // Verify key headers were recovered
        Assert.NotEmpty(decodedHeaders);
        var statusHeader = decodedHeaders.FirstOrDefault(h => h.Name == ":status");
        Assert.Equal(":status", statusHeader.Name);
        Assert.Equal("200", statusHeader.Value);

        var customHeader = decodedHeaders.FirstOrDefault(h => h.Name == "x-custom-header");
        Assert.Equal("x-custom-header", customHeader.Name);
        Assert.Equal("custom-value", customHeader.Value);
    }

    [Fact(Timeout = 5000)]
    public void ResetHpack_should_clear_encoder_state()
    {
        // Arrange
        var response1 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response1.Headers.Add("x-test", "value1");

        // Encode first response
        var frames1 = _encoder.EncodeHeaders(response1, streamId: 1, hasBody: false);
        Assert.Single(frames1);

        // Act: Reset HPACK state
        _encoder.ResetHpack();

        // Encode second response after reset
        var response2 = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new ByteArrayContent([]),
        };
        response2.Headers.Add("x-test", "value2");
        var frames2 = _encoder.EncodeHeaders(response2, streamId: 3, hasBody: false);

        // Assert: No crash occurred and frames were produced
        Assert.Single(frames2);
        var frame = Assert.IsType<HeadersFrame>(frames2[0]);
        Assert.Equal(3, frame.StreamId);

        // Verify the second encoding is correct
        var decodedHeaders = _decoder.Decode(frame.HeaderBlockFragment.Span);
        var statusHeader = decodedHeaders.FirstOrDefault(h => h.Name == ":status");
        Assert.Equal("201", statusHeader.Value);
    }
}
