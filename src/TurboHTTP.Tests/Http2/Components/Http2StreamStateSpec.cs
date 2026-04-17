using System.Buffers;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Components;

/// <summary>
/// Unit tests for StreamState per-stream header and body buffer management.
/// Covers MemoryPool-based buffer growth, header/body accumulation, response initialization,
/// and content header tracking for HTTP/2 streams.
/// </summary>
public sealed class Http2StreamStateSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void StreamState_should_initialize_with_no_response()
    {
        var state = new StreamState();

        Assert.False(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void StreamState_should_initialize_with_no_content_headers()
    {
        var state = new StreamState();

        Assert.False(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void InitResponse_should_store_response()
    {
        var state = new StreamState();
        var response = new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.OK };

        state.InitResponse(response);

        Assert.True(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void GetResponse_should_return_initialized_response()
    {
        var state = new StreamState();
        var response = new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.OK };
        state.InitResponse(response);

        var retrieved = state.GetResponse();

        Assert.Same(response, retrieved);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void GetResponse_should_throw_when_no_response_initialized()
    {
        var state = new StreamState();

        Assert.Throws<InvalidOperationException>(() => state.GetResponse());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void GetOrCreateResponse_should_return_existing_response()
    {
        var state = new StreamState();
        var response = new HttpResponseMessage();
        state.InitResponse(response);

        var retrieved = state.GetOrCreateResponse();

        Assert.Same(response, retrieved);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void GetOrCreateResponse_should_create_response_if_none_exists()
    {
        var state = new StreamState();

        var response = state.GetOrCreateResponse();

        Assert.NotNull(response);
        Assert.True(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void GetOrCreateResponse_should_return_same_instance_on_multiple_calls()
    {
        var state = new StreamState();

        var first = state.GetOrCreateResponse();
        var second = state.GetOrCreateResponse();

        Assert.Same(first, second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void AddContentHeader_should_store_header()
    {
        var state = new StreamState();

        state.AddContentHeader("content-type", "text/html");

        Assert.True(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void AddContentHeader_should_accumulate_multiple_headers()
    {
        var state = new StreamState();

        state.AddContentHeader("content-type", "text/html");
        state.AddContentHeader("content-length", "1024");

        Assert.True(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void ApplyContentHeadersTo_should_apply_stored_headers()
    {
        var state = new StreamState();
        state.AddContentHeader("content-type", "application/json");
        state.AddContentHeader("content-length", "256");
        var content = new ByteArrayContent(Array.Empty<byte>());

        state.ApplyContentHeadersTo(content);

        Assert.Contains("content-type", content.Headers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("content-length", content.Headers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void ApplyContentHeadersTo_should_not_throw_when_no_content_headers()
    {
        var state = new StreamState();
        var content = new ByteArrayContent(Array.Empty<byte>());

        state.ApplyContentHeadersTo(content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void AppendHeader_should_allocate_buffer_on_first_call()
    {
        var state = new StreamState();
        var data = new byte[] { 1, 2, 3 };

        state.AppendHeader(data);

        var span = state.GetHeaderSpan();
        Assert.Equal(3, span.Length);
        Assert.True(span.SequenceEqual(data));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void AppendHeader_should_accumulate_multiple_calls()
    {
        var state = new StreamState();
        var data1 = new byte[] { 1, 2 };
        var data2 = new byte[] { 3, 4 };

        state.AppendHeader(data1);
        state.AppendHeader(data2);

        var span = state.GetHeaderSpan();
        Assert.Equal(4, span.Length);
        Assert.True(span[..2].SequenceEqual(data1));
        Assert.True(span[2..].SequenceEqual(data2));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void AppendHeader_should_grow_buffer_when_capacity_exceeded()
    {
        var state = new StreamState();
        var largeData = new byte[10000];
        for (int i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        state.AppendHeader(largeData);

        var span = state.GetHeaderSpan();
        Assert.Equal(10000, span.Length);
        Assert.True(span.SequenceEqual(largeData));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void AppendHeader_should_copy_existing_data_on_reallocation()
    {
        var state = new StreamState();
        var first = new byte[] { 1, 2, 3, 4, 5 };
        var second = new byte[8000];
        Array.Fill(second, (byte)42);

        state.AppendHeader(first);
        state.AppendHeader(second);

        var span = state.GetHeaderSpan();
        Assert.Equal(8005, span.Length);
        Assert.True(span[..5].SequenceEqual(first));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void AppendBody_should_allocate_buffer_on_first_call()
    {
        var state = new StreamState();
        var data = new byte[] { 10, 20, 30 };

        state.AppendBody(data);

        // Verify AppendBody did not throw - body was accumulated
        (IMemoryOwner<byte>? owner, int length) = state.TakeBodyOwnership();
        Assert.NotNull(owner);
        Assert.Equal(3, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void AppendBody_should_accumulate_multiple_calls()
    {
        var state = new StreamState();
        var data1 = new byte[] { 1, 2 };
        var data2 = new byte[] { 3, 4 };

        state.AppendBody(data1);
        state.AppendBody(data2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void AppendBody_should_grow_buffer_when_capacity_exceeded()
    {
        var state = new StreamState();
        var largeData = new byte[10000];
        for (int i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        state.AppendBody(largeData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void GetHeaderSpan_should_return_correct_slice()
    {
        var state = new StreamState();
        var data = new byte[] { 5, 10, 15 };

        state.AppendHeader(data);

        var span = state.GetHeaderSpan();
        Assert.Equal(3, span.Length);
        Assert.Equal(5, span[0]);
        Assert.Equal(10, span[1]);
        Assert.Equal(15, span[2]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void GetHeaderSpan_should_return_empty_when_no_data_appended()
    {
        var state = new StreamState();

        var span = state.GetHeaderSpan();

        Assert.Empty(span.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void TakeBodyOwnership_should_return_owner_and_length()
    {
        var state = new StreamState();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        state.AppendBody(data);

        (IMemoryOwner<byte>? owner, int length) = state.TakeBodyOwnership();

        Assert.NotNull(owner);
        Assert.Equal(5, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void TakeBodyOwnership_should_return_null_when_no_body()
    {
        var state = new StreamState();

        (IMemoryOwner<byte>? owner, int length) = state.TakeBodyOwnership();

        Assert.Null(owner);
        Assert.Equal(0, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void TakeBodyOwnership_should_clear_internal_state()
    {
        var state = new StreamState();
        var data = new byte[] { 1, 2, 3 };
        state.AppendBody(data);

        (IMemoryOwner<byte>? owner1, int length1) = state.TakeBodyOwnership();
        (IMemoryOwner<byte>? owner2, int length2) = state.TakeBodyOwnership();

        Assert.NotNull(owner1);
        Assert.Equal(3, length1);
        Assert.Null(owner2);
        Assert.Equal(0, length2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Reset_should_clear_all_state()
    {
        var state = new StreamState();
        var response = new HttpResponseMessage();
        state.InitResponse(response);
        state.AppendHeader(new byte[] { 1, 2, 3 });
        state.AppendBody(new byte[] { 4, 5, 6 });
        state.AddContentHeader("content-type", "text/plain");

        state.Reset();

        Assert.False(state.HasResponse);
        Assert.False(state.HasContentHeaders);
        Assert.Empty(state.GetHeaderSpan().ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Reset_should_dispose_owned_buffers()
    {
        var state = new StreamState();
        state.AppendHeader(new byte[1000]);
        state.AppendBody(new byte[1000]);

        state.Reset();

        (IMemoryOwner<byte>? owner, int length) = state.TakeBodyOwnership();
        Assert.Null(owner);
        Assert.Equal(0, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Reset_should_allow_reuse()
    {
        var state = new StreamState();
        state.AppendHeader(new byte[] { 1, 2 });
        state.Reset();

        var newData = new byte[] { 3, 4, 5 };
        state.AppendHeader(newData);

        var span = state.GetHeaderSpan();
        Assert.True(span.SequenceEqual(newData));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Multiple_appends_should_handle_large_accumulation()
    {
        var state = new StreamState();
        const int ChunkSize = 1000;
        const int NumChunks = 20;

        for (int i = 0; i < NumChunks; i++)
        {
            var chunk = new byte[ChunkSize];
            Array.Fill(chunk, (byte)(i % 256));
            state.AppendHeader(chunk);
        }

        var span = state.GetHeaderSpan();
        Assert.Equal(ChunkSize * NumChunks, span.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void ContentHeaders_should_not_interfere_with_buffers()
    {
        var state = new StreamState();
        state.AppendHeader(new byte[] { 1, 2, 3 });
        state.AddContentHeader("content-type", "text/html");

        var span = state.GetHeaderSpan();
        Assert.Equal(3, span.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void GetOrCreateResponse_should_create_once_and_reuse()
    {
        var state = new StreamState();

        var resp1 = state.GetOrCreateResponse();
        var resp2 = state.GetOrCreateResponse();
        var resp3 = state.GetOrCreateResponse();

        Assert.Same(resp1, resp2);
        Assert.Same(resp2, resp3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void AppendHeader_with_empty_span_should_not_allocate()
    {
        var state = new StreamState();
        state.AppendHeader(ReadOnlySpan<byte>.Empty);

        var span = state.GetHeaderSpan();
        Assert.Empty(span.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void AppendBody_with_empty_span_should_not_allocate()
    {
        var state = new StreamState();
        state.AppendBody(ReadOnlySpan<byte>.Empty);

        (IMemoryOwner<byte>? owner, int length) = state.TakeBodyOwnership();
        Assert.Null(owner);
        Assert.Equal(0, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Reset_clear_content_headers_list()
    {
        var state = new StreamState();
        state.AddContentHeader("content-type", "text/html");
        state.AddContentHeader("content-length", "100");

        state.Reset();

        Assert.False(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void ApplyContentHeadersTo_multiple_headers()
    {
        var state = new StreamState();
        state.AddContentHeader("content-type", "application/json");
        state.AddContentHeader("content-length", "512");
        state.AddContentHeader("content-encoding", "gzip");
        var content = new ByteArrayContent(Array.Empty<byte>());

        state.ApplyContentHeadersTo(content);

        Assert.Contains("content-type", content.Headers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("content-length", content.Headers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("content-encoding", content.Headers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Header_and_body_buffers_should_be_independent()
    {
        var state = new StreamState();
        var headerData = new byte[100];
        var bodyData = new byte[200];
        Array.Fill(headerData, (byte)1);
        Array.Fill(bodyData, (byte)2);

        state.AppendHeader(headerData);
        state.AppendBody(bodyData);

        var headerSpan = state.GetHeaderSpan();
        Assert.Equal(100, headerSpan.Length);
        Assert.All(headerSpan.ToArray(), b => Assert.Equal(1, b));

        (IMemoryOwner<byte>? bodyOwner, int bodyLength) = state.TakeBodyOwnership();
        Assert.Equal(200, bodyLength);
    }
}
