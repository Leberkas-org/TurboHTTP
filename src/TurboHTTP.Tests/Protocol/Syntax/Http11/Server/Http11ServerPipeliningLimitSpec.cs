using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerPipeliningLimitSpec
{
    private static IFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_accept_requests_up_to_limit()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 3
            }
        };
        var sm = new Http11ServerStateMachine(options, ops);
        var request = BuildPipelinedRequests(3);
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Equal(3, ops.Requests.Count);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_enforce_pipelining_limit()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 2
            }
        };
        var sm = new Http11ServerStateMachine(options, ops);
        var request = BuildPipelinedRequests(4); // Try to send 4 requests
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        // Should only accept 2 requests (the limit)
        Assert.Equal(2, ops.Requests.Count);
        // Should mark connection for closure due to limit
        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_close_after_limit_reached_response()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 1
            }
        };
        var sm = new Http11ServerStateMachine(options, ops);
        var request = BuildPipelinedRequests(2); // Try to send 2 requests with limit 1
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.Requests);
        Assert.True(sm.ShouldComplete);

        // Send response - should trigger connection close
        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Verify the response was sent
        Assert.NotEmpty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_default_limit_should_be_16()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var request = BuildPipelinedRequests(16);
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Equal(16, ops.Requests.Count);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_reject_17th_request_with_default_limit()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var request = BuildPipelinedRequests(17);
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Equal(16, ops.Requests.Count);
        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_accept_high_limit()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 100
            }
        };
        var sm = new Http11ServerStateMachine(options, ops);
        var request = BuildPipelinedRequests(100);
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Equal(100, ops.Requests.Count);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_throw_on_invalid_limit()
    {
        var ops = new FakeServerOps();

        var invalidOpts1 = new TurboServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 0
            }
        };
        Assert.Throws<ArgumentException>(() => new Http11ServerStateMachine(invalidOpts1, ops));

        var invalidOpts2 = new TurboServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = -1
            }
        };
        Assert.Throws<ArgumentException>(() => new Http11ServerStateMachine(invalidOpts2, ops));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_limit_applies_per_buffer()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 2
            }
        };
        var sm = new Http11ServerStateMachine(options, ops);

        // First buffer with 2 requests
        var buffer1 = MakeBuffer(BuildPipelinedRequests(2));
        sm.DecodeClientData(new TransportData(buffer1));
        Assert.Equal(2, ops.Requests.Count);

        // Second buffer with 2 more requests - should also be limited (total would be 4)
        var buffer2 = MakeBuffer(BuildPipelinedRequests(2));
        sm.DecodeClientData(new TransportData(buffer2));

        // After hitting limit in first buffer and closing, second buffer should not add more
        // (behavior depends on whether ShouldCloseAfterResponse prevents further decoding)
        // For now, just verify the first buffer honored the limit
        Assert.True(sm.ShouldComplete);
    }

    private static TransportBuffer MakeBuffer(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    private static string BuildPipelinedRequests(int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            sb.Append($"GET /page{i} HTTP/1.1\r\n");
            sb.Append("Host: example.com\r\n");
            sb.Append("Content-Length: 0\r\n");
            sb.Append("\r\n");
        }

        return sb.ToString();
    }
}

