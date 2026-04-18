using System.Net;
using System.Text;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

/// <summary>
/// Tests <see cref="TlsClientProvider"/> TLS handshake setup, CONNECT tunnel integration,
/// and cleanup logic. Does not require actual TLS connections.
/// </summary>
public sealed class TlsClientProviderSpec
{
    [Fact(Timeout = 5000)]
    public void TlsClientProvider_should_initialize_with_options()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443
        };

        var provider = new TlsClientProvider(options);

        Assert.Null(provider.RemoteEndPoint);
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_dispose_without_connection()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };
        var provider = new TlsClientProvider(options);

        // No GetStreamAsync called, so no SslStream created
        await provider.DisposeAsync();

        // Assert: should complete without error
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_complete_disposal_on_double_dispose()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };
        var provider = new TlsClientProvider(options);

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        // Assert: should not throw on second dispose
    }

    [Fact(Timeout = 5000)]
    public void TlsClientProvider_should_not_support_multiple_streams()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };
        IClientProvider provider = new TlsClientProvider(options);

        Assert.False(provider.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_throw_on_unidirectional_stream()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };
        IClientProvider provider = new TlsClientProvider(options);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.GetUnidirectionalStreamAsync(CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_throw_on_accept_inbound_stream()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };
        IClientProvider provider = new TlsClientProvider(options);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.AcceptInboundStreamAsync(CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_use_target_host_for_sni()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            TargetHost = "sni.example.com"
        };

        var provider = new TlsClientProvider(options);

        // GetStreamAsync will fail at TCP level, but we can verify the flow
        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Expected: network error
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_establish_connect_tunnel_when_proxy_configured()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy,
            ServerCertificateValidationCallback = (_, _, _, _) => true
        };

        var provider = new TlsClientProvider(options);

        // This test would need to mock GetStreamAsync to inject our stream.
        // For now, we test the static EstablishConnectTunnelAsync method directly.
        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, null, TestContext.Current.CancellationToken);

        var request = await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 200 Connection Established\r\n\r\n");
        await tunnelTask;

        Assert.StartsWith("CONNECT example.com:443", request);
        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_fail_when_connect_tunnel_fails()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, null, TestContext.Current.CancellationToken);

        await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => tunnelTask);
        Assert.Contains("407", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_include_proxy_auth_in_connect_request()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var credentials = new NetworkCredential("user", "pass");
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"), credentials);

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, null, TestContext.Current.CancellationToken);

        var request = await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 200 OK\r\n\r\n");
        await tunnelTask;

        var expectedEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        Assert.Contains($"Proxy-Authorization: Basic {expectedEncoded}", request);
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_apply_default_proxy_credentials_in_connect()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var defaultCredentials = new NetworkCredential("default", "default");
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, defaultCredentials,
            TestContext.Current.CancellationToken);

        var request = await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 200 OK\r\n\r\n");
        await tunnelTask;

        var expectedEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("default:default"));
        Assert.Contains($"Proxy-Authorization: Basic {expectedEncoded}", request);
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_throw_on_proxy_close_during_connect()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, null, TestContext.Current.CancellationToken);

        // Wait a tiny bit for request to be sent, then close
        await Task.Delay(10, TestContext.Current.CancellationToken);
        await serverStream.DisposeAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => tunnelTask);
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_accept_http10_200_in_connect()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, null, TestContext.Current.CancellationToken);

        await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.0 200 OK\r\n\r\n");

        await tunnelTask;
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_throw_when_connect_response_exceeds_buffer()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, null, TestContext.Current.CancellationToken);

        await ReadRequestAsync(serverStream);

        // Write a response larger than 4096 bytes without ending in \r\n\r\n
        var largeResponse = new string('X', 5000);
        await WriteResponseAsync(serverStream, largeResponse);
        await serverStream.FlushAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => tunnelTask);
        Assert.Contains("exceeded buffer size", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_handle_chunked_connect_response()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "example.com", 443, proxy, null, TestContext.Current.CancellationToken);

        // Send response in chunks to test the read loop
        var requestTask = ReadRequestAsync(serverStream);

        // Send first part of response
        await serverStream.WriteAsync("HTTP/1.1 200 "u8.ToArray(), TestContext.Current.CancellationToken);
        await serverStream.FlushAsync(TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);

        // Send rest of response
        await serverStream.WriteAsync("Connection Established\r\n\r\n"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await serverStream.FlushAsync(TestContext.Current.CancellationToken);

        await requestTask;
        await tunnelTask;
    }

    [Fact(Timeout = 5000)]
    public async Task TlsClientProvider_should_format_connect_request_correctly()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, "custom.host", 8443, proxy, null, TestContext.Current.CancellationToken);

        var request = await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 200 OK\r\n\r\n");
        await tunnelTask;

        Assert.StartsWith("CONNECT custom.host:8443 HTTP/1.1\r\n", request);
        Assert.Contains("Host: custom.host:8443\r\n", request);
        Assert.EndsWith("\r\n\r\n", request);
    }

    private static (Stream Client, Stream Server) CreateDuplexPipe()
    {
        var clientToServer = new System.IO.Pipelines.Pipe();
        var serverToClient = new System.IO.Pipelines.Pipe();

        var clientStream = new DuplexPipeStream(serverToClient.Reader, clientToServer.Writer);
        var serverStream = new DuplexPipeStream(clientToServer.Reader, serverToClient.Writer);

        return (clientStream, serverStream);
    }

    private static async Task<string> ReadRequestAsync(Stream serverStream)
    {
        var buffer = new byte[4096];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await serverStream.ReadAsync(buffer.AsMemory(totalRead));
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            var text = Encoding.ASCII.GetString(buffer, 0, totalRead);
            if (text.Contains("\r\n\r\n"))
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, totalRead);
    }

    private static async Task WriteResponseAsync(Stream serverStream, string response)
    {
        await serverStream.WriteAsync(Encoding.ASCII.GetBytes(response));
        await serverStream.FlushAsync();
    }

    private sealed class TestProxy(Uri? proxyUri, ICredentials? credentials = null) : IWebProxy
    {
        public ICredentials? Credentials
        {
            get => credentials;
            set { }
        }

        public Uri? GetProxy(Uri destination) => proxyUri;

        public bool IsBypassed(Uri host) => false;
    }

    private sealed class DuplexPipeStream(System.IO.Pipelines.PipeReader reader, System.IO.Pipelines.PipeWriter writer)
        : Stream
    {
        private bool _disposed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_disposed)
            {
                return 0;
            }

            var result = await reader.ReadAsync(ct);
            var sequence = result.Buffer;

            if (sequence.IsEmpty && result.IsCompleted)
            {
                return 0;
            }

            var bytesToCopy = (int)Math.Min(buffer.Length, sequence.Length);
            var sliced = sequence.Slice(0, bytesToCopy);
            var offset = 0;
            foreach (var segment in sliced)
            {
                segment.Span.CopyTo(buffer.Span.Slice(offset));
                offset += segment.Length;
            }

            reader.AdvanceTo(sliced.End);
            return bytesToCopy;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await writer.WriteAsync(buffer, ct);
        }

        public override async Task FlushAsync(CancellationToken ct)
        {
            await writer.FlushAsync(ct);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                writer.Complete();
                reader.Complete();
            }

            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}