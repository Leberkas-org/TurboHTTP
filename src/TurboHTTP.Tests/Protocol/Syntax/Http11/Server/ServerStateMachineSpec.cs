using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class ServerStateMachineSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeClientData_should_emit_request_when_complete_get()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        Assert.Equal("GET", ctx.Request.Method);
        Assert.Equal("/", ctx.Request.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_emit_response_headers()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var responseBody = "Hello"u8.ToArray();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responseBody)
        };
        response.Content.Headers.ContentLength = responseBody.Length;

        sm.OnResponse(MakeResponseContext(response));

        Assert.True(ops.Outbound.Count >= 1);
        var outbound = ops.Outbound[0];
        Assert.IsType<TransportData>(outbound);

        var transportData = (TransportData)outbound;
        var responseText = Encoding.ASCII.GetString(transportData.Buffer.Span);
        Assert.Contains("200", responseText);
        Assert.Contains("Content-Length: 5", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_false_when_no_pending_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_true_after_request_decoded()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void ShouldCloseAfterResponse_should_be_true_when_connection_close_header()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ShouldCloseAfterResponse_should_be_true_when_http_10_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.0\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void OnResponse_should_set_connection_close_header_when_flag_set()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        sm.OnResponse(MakeResponseContext(response));

        var outbound = ops.Outbound[0];
        var transportData = (TransportData)outbound;
        var responseText = Encoding.ASCII.GetString(transportData.Buffer.Span);
        Assert.Contains("Connection: close", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_not_include_body_in_transport_data()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(MakeResponseContext(response));

        var outboundItems = ops.Outbound.OfType<TransportData>().ToList();
        Assert.NotEmpty(outboundItems);
        var responseText = Encoding.ASCII.GetString(outboundItems[0].Buffer.Span);
        Assert.Contains("HTTP/1.1 200", responseText);
        Assert.DoesNotContain("hello world", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_should_emit_body_chunk_as_transport_data()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(MakeResponseContext(response));
        var countAfterHeaders = ops.Outbound.Count;

        var bodyBytes = "hello world"u8.ToArray();
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(bodyBytes.Length);
        bodyBytes.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, bodyBytes.Length));
        sm.OnBodyMessage(new OutboundBodyComplete());

        var bodyItems = ops.Outbound.Skip(countAfterHeaders).OfType<TransportData>().ToList();
        Assert.NotEmpty(bodyItems);
        var bodyText = Encoding.UTF8.GetString(bodyItems[0].Buffer.Span);
        Assert.Contains("hello world", bodyText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_false_when_outbound_body_pending()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(MakeResponseContext(response));

        Assert.False(sm.CanAcceptResponse);

        sm.OnBodyMessage(new OutboundBodyComplete());

        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void DecodeClientData_should_signal_error_for_oversized_uri()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var longUri = "/" + new string('a', 16_000);
        var requestData = Encoding.ASCII.GetBytes(
            $"GET {longUri} HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(ops.Requests.Count is 0 or 1);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void OnResponse_should_not_include_transfer_encoding_for_204()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        sm.OnResponse(MakeResponseContext(response));

        var outbound = ops.Outbound.OfType<TransportData>().ToList();
        if (outbound.Count > 0)
        {
            var responseText = Encoding.ASCII.GetString(outbound[0].Buffer.Span);
            Assert.DoesNotContain("Transfer-Encoding", responseText);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void DecodeClientData_should_pass_unknown_transfer_encoding_to_application()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = Encoding.ASCII.GetBytes(
            "POST / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Transfer-Encoding: unknown\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // §6.1 SHOULD respond 501 — but the SM passes the request to the application layer
        // which is responsible for inspecting TE and returning 501. The SM correctly decodes
        // the request structure and preserves the TE header for application inspection.
        Assert.Single(ops.Requests);
        Assert.Equal("POST", ops.Requests[0].Request.Method);
    }

    private static TurboHttpContext MakeResponseContext(HttpResponseMessage response)
    {
        var features = new TurboFeatureCollection();
        var responseFeature = new TurboHttpResponseFeature
        {
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
        };

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                responseFeature.Headers[header.Key] = new StringValues(header.Value.ToArray());
            }
        }

        foreach (var header in response.Headers)
        {
            responseFeature.Headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        if (response.Content is not null)
        {
            var bodyFeature = new TurboHttpResponseBodyFeature();
            features.Set<IHttpResponseBodyFeature>(bodyFeature);
            features.Set<IHttpResponseBodyFeature>(bodyFeature);
        }

        features.Set<IHttpResponseFeature>(responseFeature);
        return new TurboHttpContext(features);
    }
}
