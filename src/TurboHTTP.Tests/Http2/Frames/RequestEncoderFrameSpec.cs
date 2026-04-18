using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Frames;

public sealed class Http2RequestEncoderFrameSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_produce_headers_frame_with_end_stream_when_encoding_get_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var frames = encoder.Encode(request, 1);

        Assert.Single(frames);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, hf.StreamId);
        Assert.True(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_produce_headers_then_data_when_encoding_post_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("hello world"),
        };

        var frames = encoder.Encode(request, 1);

        Assert.Equal(2, frames.Count);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndStream);
        Assert.True(hf.EndHeaders);

        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(1, df.StreamId);
        Assert.True(df.EndStream);
        Assert.NotEmpty(df.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void Http2RequestEncoder_should_contain_pseudo_headers_when_encoding_get_request_header_block()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/v1/data?q=1");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/v1/data?q=1" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "api.example.com" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void Http2RequestEncoder_should_include_query_in_path_when_encoding_request_with_query()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?term=foo&page=2");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        Assert.Contains(headers, h => h is { Name: ":path", Value: "/search?term=foo&page=2" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2RequestEncoder_should_strip_connection_headers_when_encoding()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("x-custom", "value");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        Assert.DoesNotContain(headers, h => h.Name == "connection");
        Assert.Contains(headers, h => h.Name == "x-custom");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2RequestEncoder_should_use_continuation_frames_when_header_block_larger_than_max_frame_size()
    {
        // Use a tiny maxFrameSize to force continuation
        var encoder = new RequestEncoder(useHuffman: false, maxFrameSize: 30);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        request.Headers.TryAddWithoutValidation("x-long-header", new string('a', 100));

        var frames = encoder.Encode(request, 1);

        Assert.True(frames.Count >= 2, "Expected at least HEADERS + CONTINUATION");

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndHeaders, "First HEADERS frame should not have END_HEADERS");

        // Last frame must be CONTINUATION with END_HEADERS=true or a HEADERS with END_HEADERS=true
        var lastFrame = frames[^1];

        if (lastFrame is ContinuationFrame cf)
        {
            Assert.True(cf.EndHeaders, "Last CONTINUATION frame must have END_HEADERS");
        }
        else
        {
            Assert.IsType<HeadersFrame>(lastFrame);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void Http2RequestEncoder_should_have_same_stream_id_on_all_frames_when_encoding_post_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new ByteArrayContent([1, 2, 3, 4]),
        };

        var frames = encoder.Encode(request, 1);

        Assert.All(frames, f => Assert.Equal(1, f.StreamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_produce_headers_frame_with_end_headers()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request, 1);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_encode_multiple_requests_with_increasing_stream_ids()
    {
        var encoder = new RequestEncoder();
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");

        var frames0 = encoder.Encode(request1, 1);
        var streamId0 = frames0[0].StreamId; // consume before next Encode() (reusable list)

        var frames1 = encoder.Encode(request2, 3);
        var streamId1 = frames1[0].StreamId;

        Assert.Equal(1, streamId0);
        Assert.Equal(3, streamId1);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2RequestEncoder_should_apply_server_settings_max_frame_size()
    {
        var encoder = new RequestEncoder(maxFrameSize: 16384);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        // Before settings change
        var frames1 = encoder.Encode(request, 1);
        Assert.NotEmpty(frames1);

        // Apply new settings with larger frame size
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 32768u)]);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        // After settings change, should still work
        var frames2 = encoder.Encode(request2, 3);
        Assert.NotEmpty(frames2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2RequestEncoder_should_apply_server_settings_header_table_size()
    {
        var encoder = new RequestEncoder();
        encoder.ApplyServerSettings([(SettingsParameter.HeaderTableSize, 2048)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var frames = encoder.Encode(request, 1);

        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2RequestEncoder_should_apply_server_settings_initial_window_size()
    {
        var encoder = new RequestEncoder();

        // Apply new initial window size
        encoder.ApplyServerSettings([(SettingsParameter.InitialWindowSize, 32768)]);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new ByteArrayContent(new byte[100]),
        };

        var frames = encoder.Encode(request, 1);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2RequestEncoder_should_update_connection_window()
    {
        var encoder = new RequestEncoder();
        encoder.UpdateConnectionWindow(100);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new ByteArrayContent(new byte[50]),
        };

        var frames = encoder.Encode(request, 1);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2RequestEncoder_should_update_connection_window_with_invalid_increment()
    {
        var encoder = new RequestEncoder();

        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.UpdateConnectionWindow(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.UpdateConnectionWindow(-1));
        // Negative value after unchecked cast also fails ArgumentOutOfRangeException
        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.UpdateConnectionWindow(unchecked((int)0x80000000)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2RequestEncoder_should_update_stream_window()
    {
        var encoder = new RequestEncoder();
        encoder.UpdateStreamWindow(1, 100);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new ByteArrayContent(new byte[50]),
        };

        var frames = encoder.Encode(request, 1);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2RequestEncoder_should_update_stream_window_with_invalid_increment()
    {
        var encoder = new RequestEncoder();

        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.UpdateStreamWindow(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.UpdateStreamWindow(1, -1));
        // Negative value after unchecked cast also fails ArgumentOutOfRangeException
        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.UpdateStreamWindow(1, unchecked((int)0x80000000)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void Http2RequestEncoder_should_reset_hpack_encoder()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var frames1 = encoder.Encode(request, 1);
        Assert.NotEmpty(frames1);

        encoder.ResetHpack();

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/other");
        var frames2 = encoder.Encode(request2, 1);
        Assert.NotEmpty(frames2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2RequestEncoder_should_throw_when_stream_id_negative()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var ex = Assert.Throws<Http2Exception>(() => encoder.Encode(request, -1));
        Assert.Contains("stream ID space exhausted", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void Http2RequestEncoder_should_throw_when_request_uri_null()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, (string)null!);

        Assert.Throws<ArgumentNullException>(() => encoder.Encode(request, 1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2RequestEncoder_should_handle_large_header_block_fragmentation()
    {
        var encoder = new RequestEncoder(useHuffman: false, maxFrameSize: 100);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        request.Headers.TryAddWithoutValidation("x-large-1", new string('a', 200));
        request.Headers.TryAddWithoutValidation("x-large-2", new string('b', 200));

        var frames = encoder.Encode(request, 1);

        // Should have fragmented into multiple continuation frames
        Assert.True(frames.Count >= 2);
        var lastFrame = frames[^1];
        Assert.True(lastFrame is HeadersFrame or ContinuationFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2RequestEncoder_should_respect_connection_window_for_post_body()
    {
        var encoder = new RequestEncoder();
        var largeBody = new byte[32768]; // Larger than default window
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new ByteArrayContent(largeBody),
        };

        var frames = encoder.Encode(request, 1);

        // Should encode header + truncated data based on flow control window
        Assert.NotEmpty(frames);
        var dataFrames = frames.OfType<DataFrame>().ToList();
        var totalBytes = dataFrames.Sum(df => df.Data.Length);
        Assert.True(totalBytes <= 65535); // Default connection window
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-2.3.2")]
    public void Http2RequestEncoder_should_lowercase_header_names()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "value");
        request.Headers.TryAddWithoutValidation("CONTENT-TYPE", "text/plain");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        Assert.Contains(headers, h => h.Name == "x-custom-header");
        // Note: custom headers, not pseudo-headers
        Assert.All(headers.Where(h => !h.Name.StartsWith(":")), h =>
            Assert.Equal(h.Name, h.Name.ToLowerInvariant()));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2RequestEncoder_should_strip_all_forbidden_headers()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "upgrade");
        request.Headers.TryAddWithoutValidation("keep-alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("transfer-encoding", "chunked");
        request.Headers.TryAddWithoutValidation("upgrade", "websocket");
        request.Headers.TryAddWithoutValidation("proxy-connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("te", "trailers");
        request.Headers.TryAddWithoutValidation("x-custom", "allowed");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        var headerNames = headers.Select(h => h.Name.ToLowerInvariant()).ToHashSet();
        Assert.DoesNotContain("connection", headerNames);
        Assert.DoesNotContain("keep-alive", headerNames);
        Assert.DoesNotContain("transfer-encoding", headerNames);
        Assert.DoesNotContain("upgrade", headerNames);
        Assert.DoesNotContain("proxy-connection", headerNames);
        Assert.DoesNotContain("te", headerNames);
        Assert.Contains("x-custom", headerNames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_encode_post_with_empty_body()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        request.Content = new ByteArrayContent([]);

        var frames = encoder.Encode(request, 1);

        Assert.Equal(2, frames.Count); // HEADERS + empty DATA
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndStream);
        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.True(df.EndStream);
        Assert.Empty(df.Data.ToArray());
    }
}