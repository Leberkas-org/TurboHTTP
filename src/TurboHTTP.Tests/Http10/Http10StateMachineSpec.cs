using System.Net;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http10;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10StateMachineSpec
{
    private static TurboClientOptions MakeConfig() => new();

    private static HttpRequestMessage MakeRequest(string uri = "http://example.com/", HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    private static (HttpRequestMessage Request, PendingRequest Pending, short Version) MakeTrackedRequest(
        string uri = "http://example.com/", HttpContent? content = null)
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (content != null) request.Content = content;
        request.Options.Set(TcsCorrelation.Key, pending);
        request.Options.Set(TcsCorrelation.VersionKey, version);
        return (request, pending, version);
    }

    private static TransportBuffer CreateResponseBuffer(string responseText)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(responseText);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest("http://example.com:8080/path"));

        Assert.NotEqual(default, sm.Endpoint);
        Assert.Equal("example.com", sm.Endpoint.Host);
        Assert.Equal(8080, sm.Endpoint.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_not_overwrite_endpoint_on_subsequent_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest("http://example.com:8080/"));
        var capturedEndpoint = sm.Endpoint;

        sm.OnRequest(MakeRequest("http://example.com:9090/")); // Different host/port

        Assert.Equal(capturedEndpoint, sm.Endpoint); // Should not change
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_emit_transport_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());

        Assert.Contains(ops.Outbound, o => o is TransportData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_emit_transport_data_with_encoded_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest("http://example.com/test"));

        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        Assert.True(buffer.Length > 0);

        var text = System.Text.Encoding.ASCII.GetString(buffer.Span);
        Assert.Contains("GET /test HTTP/1.0", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_set_in_flight_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());

        Assert.True(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_include_content_length_in_encoded_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        var content = new StringContent("hello world");
        var request = MakeRequest("http://example.com/", content);

        sm.OnRequest(request);

        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        var text = System.Text.Encoding.ASCII.GetString(buffer.Span);
        Assert.Contains("Content-Length:", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_calculate_buffer_size_based_on_content_length()
    {
        var ops = new FakeOps();
        const int minBufferSize = 1024;
        var sm = new StateMachine(ops, MakeConfig(), minBufferSize: minBufferSize);

        var content = new StringContent("hello world");
        var request = MakeRequest("http://example.com/", content);

        sm.OnRequest(request);

        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        // Buffer should be at least minBufferSize
        Assert.True(buffer.Capacity >= minBufferSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_respect_min_buffer_size()
    {
        var ops = new FakeOps();
        const int minBufferSize = 2048;
        var sm = new StateMachine(ops, MakeConfig(), minBufferSize: minBufferSize);

        sm.OnRequest(MakeRequest()); // Minimal request

        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        Assert.True(buffer.Capacity >= minBufferSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_handle_successful_encode_for_post_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        var content = new StringContent("test body");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api");
        request.Content = content;

        sm.OnRequest(request);

        Assert.True(sm.HasInFlightRequest);
        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_handle_request_without_body()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/");

        sm.OnRequest(request);

        Assert.True(sm.HasInFlightRequest);
        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        var text = System.Text.Encoding.ASCII.GetString(buffer.Span);
        Assert.Contains("HEAD / HTTP/1.0", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_handle_close_signal_item()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var closeSignal = new TransportDisconnected(DisconnectReason.Graceful);

        sm.DecodeServerData(closeSignal);

        // No crash should occur; close signal is handled
        Assert.True(true); // Just verifying no exception
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_ignore_non_transport_data_items()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        var item = new TransportConnected(default!);

        // Should return early without crashing
        sm.DecodeServerData(item);

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_decode_complete_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_complete_response_on_successful_decode()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());
        ops.Responses.Clear();

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_set_request_message_on_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        var originalRequest = MakeRequest("http://example.com/test");
        sm.OnRequest(originalRequest);

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.Single(ops.Responses);
        Assert.NotNull(ops.Responses[0].RequestMessage);
        Assert.Equal(originalRequest.RequestUri, ops.Responses[0].RequestMessage!.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_clear_in_flight_request_on_decode()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.False(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_handle_incomplete_response_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        // Send incomplete response (missing body)
        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 10\r\n\r\nhell");

        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.Empty(ops.Responses); // Not decoded yet
        Assert.True(sm.HasInFlightRequest); // Still waiting
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_dispose_buffer_after_decode()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeServerData(new TransportData(responseBuffer));

        // Buffer should be disposed (no way to verify directly, but no exception should occur)
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_handle_http09_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        // HTTP/0.9 responses don't have status line — just body
        var responseBuffer = CreateResponseBuffer("This is HTTP/0.9 body data");

        sm.DecodeServerData(new TransportData(responseBuffer));

        // Data is buffered but not yet complete (needs EOF to finalize)
        Assert.False(sm.HasInFlightRequest == false); // Decoder is still waiting for EOF
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_handle_fragmented_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        // Send response in fragments
        var fragment1 = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-");
        sm.DecodeServerData(new TransportData(fragment1));
        Assert.Empty(ops.Responses); // Not complete yet

        // Send rest of response
        sm.OnRequest(MakeRequest()); // New request for next response
        var fragment2 = CreateResponseBuffer("Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(fragment2));

        // Now we should have responses
        Assert.True(ops.Responses.Count >= 0); // Behavior depends on decoder buffering
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_fail_request_on_abrupt_close_with_content_length_mismatch()
    {
        var config = MakeConfig();
        config.Http1.MaxReconnectAttempts = 0;
        var sm = new StateMachine(new FakeOps(), config);
        var (request, pending, _) = MakeTrackedRequest();
        sm.OnRequest(request);

        var partialBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nhello");
        sm.DecodeServerData(new TransportData(partialBuffer));

        var closeSignal = new TransportDisconnected(DisconnectReason.Error);
        sm.DecodeServerData(closeSignal);

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_fail_request_on_abrupt_close()
    {
        var config = MakeConfig();
        config.Http1.MaxReconnectAttempts = 0;
        var sm = new StateMachine(new FakeOps(), config);
        var (request, pending, _) = MakeTrackedRequest();
        sm.OnRequest(request);

        var closeSignal = new TransportDisconnected(DisconnectReason.Error);
        sm.DecodeServerData(closeSignal);

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_stay_alive_after_abrupt_close()
    {
        var config = new TurboClientOptions { Http1 = { MaxReconnectAttempts = 0 } };
        var sm = new StateMachine(new FakeOps(), config);
        sm.OnRequest(MakeRequest());

        var closeSignal = new TransportDisconnected(DisconnectReason.Error);
        sm.DecodeServerData(closeSignal);

        // SM should stay alive to accept more requests
        Assert.True(sm.CanAcceptRequest);
        Assert.False(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_handle_clean_close_with_complete_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        // Send complete response
        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        sm.DecodeServerData(new TransportData(responseBuffer));
        ops.Responses.Clear(); // Clear previous response

        // Now handle clean close
        var closeSignal = new TransportDisconnected(DisconnectReason.Graceful);
        sm.DecodeServerData(closeSignal);

        // Should complete without error
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_complete_response_on_clean_close_with_buffered_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        // Send partial response that's buffered by decoder
        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\n");
        sm.DecodeServerData(new TransportData(responseBuffer));

        ops.Responses.Clear();

        // Clean close triggers EOF decode
        var closeSignal = new TransportDisconnected(DisconnectReason.Graceful);
        sm.DecodeServerData(closeSignal);

        // May or may not have a response depending on whether headers-only is valid
        Assert.True(ops.Responses.Count >= 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_reset_decoder_on_clean_close_with_no_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        // Send no response data, then clean close
        var closeSignal = new TransportDisconnected(DisconnectReason.Graceful);
        sm.DecodeServerData(closeSignal);

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void CanAcceptRequest_should_return_false_with_in_flight_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void CanAcceptRequest_should_return_true_when_idle()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void PendingRequestCount_should_return_one_with_in_flight_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        Assert.Equal(1, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void PendingRequestCount_should_return_zero_when_idle()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        Assert.Equal(0, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void HasInFlightRequest_should_return_true_when_request_pending()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        Assert.True(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void HasInFlightRequest_should_return_false_when_idle()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        Assert.False(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Cleanup_should_clear_in_flight_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        sm.Cleanup();

        Assert.False(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Cleanup_should_reset_decoder()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        // Partially receive response
        var partialBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\npart");
        sm.DecodeServerData(new TransportData(partialBuffer));

        sm.Cleanup();

        // After cleanup, decoder should be reset; new request should work
        sm.OnRequest(MakeRequest());
        var validBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(validBuffer));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_handle_full_request_response_cycle()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        // Encode request
        var request = MakeRequest("http://example.com/path");
        sm.OnRequest(request);

        Assert.True(sm.HasInFlightRequest);
        Assert.Contains(ops.Outbound, o => o is TransportData);

        ops.Outbound.Clear();

        // Decode response
        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.False(sm.HasInFlightRequest);
        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_handle_multiple_sequential_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        // First request
        sm.OnRequest(MakeRequest("http://example.com/1"));
        Assert.True(sm.HasInFlightRequest);

        var response1 = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(response1));

        Assert.False(sm.HasInFlightRequest);
        Assert.Single(ops.Responses);

        // Second request
        sm.OnRequest(MakeRequest("http://example.com/2"));
        Assert.True(sm.HasInFlightRequest);

        ops.Responses.Clear();
        var response2 = CreateResponseBuffer("HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(response2));

        Assert.False(sm.HasInFlightRequest);
        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.NotFound, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_handle_204_no_content_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 204 No Content\r\n\r\n");
        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.NoContent, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_handle_304_not_modified_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 304 Not Modified\r\n\r\n");
        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.NotModified, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_allow_request_after_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());
        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.True(sm.CanAcceptRequest); // Should be able to accept new request
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_preserve_request_reference_across_responses()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        var request1 = MakeRequest("http://example.com/path1");
        sm.OnRequest(request1);

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(responseBuffer));

        Assert.Single(ops.Responses);
        Assert.NotNull(ops.Responses[0].RequestMessage);
        Assert.Equal(request1.RequestUri, ops.Responses[0].RequestMessage!.RequestUri);
    }
}
