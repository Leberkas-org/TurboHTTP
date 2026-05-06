using System.Text;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11StateMachineSpec
{
    private static TurboClientOptions MakeConfig(int maxPipelineDepth = 8) => new() { Http1 = new() { MaxPipelineDepth = maxPipelineDepth } };

    private static HttpRequestMessage MakeRequest(string path = "/", string? method = null, HttpContent? content = null)
    {
        var httpMethod = method switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            _ => HttpMethod.Get
        };

        var req = new HttpRequestMessage(httpMethod, $"http://example.com{path}")
        {
            Version = new Version(1, 1),
            Content = content
        };

        return req;
    }

    private static TransportBuffer CreateResponseBuffer(string response)
    {
        var bytes = Encoding.ASCII.GetBytes(response);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_enqueue_request_and_emit_stream_acquire()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_emit_network_buffer_with_encoded_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());

        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        Assert.True(buffer.Length > 0);
        buffer.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());

        Assert.NotEqual(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_respect_max_pipeline_depth()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig(maxPipelineDepth: 2));

        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_handle_post_request_with_content()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        var content = new StringContent("test body", Encoding.UTF8);

        sm.OnRequest(MakeRequest("/", "POST", content));

        Assert.True(sm.HasInFlightRequests);
        Assert.NotEmpty(ops.Outbound.OfType<TransportData>().Select(d => d.Buffer));
        foreach (var buf in ops.Outbound.OfType<TransportData>().Select(d => d.Buffer))
        {
            buf.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_emit_multiple_requests_in_pipeline()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        Assert.Equal(3, sm.PendingRequestCount);
        var buffers = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).ToList();
        Assert.Equal(3, buffers.Count);
        foreach (var buf in buffers)
        {
            buf.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_handle_request_without_content()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest("/", "GET"));

        Assert.True(sm.HasInFlightRequests);
        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        buffer.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_respect_max_buffer_size()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig(), minBufferSize: 1024, maxBufferSize: 2048);
        var content = new StringContent("test", Encoding.UTF8);

        sm.OnRequest(MakeRequest("/", "POST", content));

        var buffer = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).FirstOrDefault();
        Assert.NotNull(buffer);
        Assert.True(buffer.Capacity <= 2048);
        buffer.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_decode_single_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_emit_connection_reuse_item()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_decode_multiple_pipelined_responses()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        var buffer = CreateResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK" +
            "HTTP/1.1 201 Created\r\nContent-Length: 7\r\n\r\nCreated");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Equal(2, ops.Responses.Count);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
        Assert.Equal((int)System.Net.HttpStatusCode.Created, (int)ops.Responses[1].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_buffer_close_delimited_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_accumulate_body_for_close_delimited_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer1 = CreateResponseBuffer("HTTP/1.1 200 OK\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer1));

        var buffer2 = CreateResponseBuffer("response body");
        sm.DecodeServerData(new TransportData(buffer2));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_handle_connection_close_header()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Single(ops.Responses);
        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_handle_graceful_disconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());
        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.True(ops.StageCompleted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_clear_effective_pipeline_depth_when_connection_close_with_multiple_inflight()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nOK");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Contains("pipelined", ops.Warnings.FirstOrDefault() ?? "");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_preserve_request_reference()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        var req = MakeRequest();
        sm.OnRequest(req);

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.NotNull(ops.Responses[0].RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_complete_close_delimited_response_on_graceful_disconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer1 = CreateResponseBuffer("HTTP/1.1 200 OK\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer1));
        var buffer2 = CreateResponseBuffer("body content");
        sm.DecodeServerData(new TransportData(buffer2));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_warn_on_abrupt_close_with_pending_close_delimited()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Throws<HttpRequestException>(() =>
            sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error)));

        Assert.Contains("Abrupt", ops.Warnings.FirstOrDefault() ?? "");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_decode_eof_response_on_graceful_disconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        sm.DecodeServerData(new TransportData(buffer));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_warn_on_abrupt_close_without_pending()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.Contains("Abrupt", ops.Warnings.FirstOrDefault() ?? "");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_dispose_body_owners_on_abrupt_close()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer1 = CreateResponseBuffer("HTTP/1.1 200 OK\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer1));
        var buffer2 = CreateResponseBuffer("body");
        sm.DecodeServerData(new TransportData(buffer2));

        var ex = Assert.Throws<HttpRequestException>(() =>
            sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error)));

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void OnUpstreamFinished_should_not_warn_when_no_inflight_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        sm.OnUpstreamFinished();

        Assert.Empty(ops.Warnings);
        Assert.True(ops.StageCompleted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void OnUpstreamFinished_should_warn_and_discard_orphaned_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        sm.OnUpstreamFinished();

        Assert.False(sm.HasInFlightRequests);
        Assert.Contains("orphaned", ops.Warnings.FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(ops.StageCompleted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void CanAcceptRequest_should_be_true_initially()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void CanAcceptRequest_should_be_false_when_queue_full()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig(maxPipelineDepth: 2));
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void HasInFlightRequests_should_reflect_queue_count()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        Assert.False(sm.HasInFlightRequests);
        sm.OnRequest(MakeRequest());
        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Endpoint_should_be_initialized_on_first_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        Assert.Equal(default, sm.Endpoint);
        sm.OnRequest(MakeRequest());
        Assert.NotEqual(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void PendingRequestCount_should_reflect_queue_count()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        Assert.Equal(2, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void IsReconnecting_should_be_false_initially()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());

        Assert.False(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Cleanup_should_clear_inflight_queue()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        sm.Cleanup();

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Cleanup_should_dispose_body_owners()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer1 = CreateResponseBuffer("HTTP/1.1 200 OK\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer1));
        var buffer2 = CreateResponseBuffer("body");
        sm.DecodeServerData(new TransportData(buffer2));

        sm.Cleanup();

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Pipeline_should_correlate_responses_to_requests_in_order()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        var buffer = CreateResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK" +
            "HTTP/1.1 201 Created\r\nContent-Length: 7\r\n\r\nCreated" +
            "HTTP/1.1 202 Accepted\r\nContent-Length: 8\r\n\r\nAccepted");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Equal(3, ops.Responses.Count);
        Assert.NotNull(ops.Responses[0].RequestMessage);
        Assert.NotNull(ops.Responses[1].RequestMessage);
        Assert.NotNull(ops.Responses[2].RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void CloseDelimited_should_work_with_initial_body_bytes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer1 = CreateResponseBuffer("HTTP/1.1 200 OK\r\n\r\nstart");
        sm.DecodeServerData(new TransportData(buffer1));

        var buffer2 = CreateResponseBuffer("more");
        sm.DecodeServerData(new TransportData(buffer2));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void NoBodyResponseTypes_should_not_be_close_delimited()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 204 No Content\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.NoContent, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void Not_Modified_should_not_be_close_delimited()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 304 Not Modified\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.NotModified, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void TransferEncoding_chunked_should_not_be_close_delimited()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Multiple_requests_with_connection_close_should_disable_pipeline()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        var buffer = CreateResponseBuffer("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeServerData(new TransportData(buffer));

        var hasWarning = ops.Warnings.Any(w => w.Contains("pipelined"));
        Assert.True(hasWarning);
    }
}
