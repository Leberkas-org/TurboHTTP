using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

public sealed class ConnectTunnelSpec
{
    private const string TargetHost = "example.com";
    private const int TargetPort = 443;

    [Fact(Timeout = 10_000)]
    public async Task Tunnel_should_send_correct_CONNECT_request()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, TargetHost, TargetPort,
            new SimpleProxy(), null, TestContext.Current.CancellationToken);

        var request = await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 200 Connection Established\r\n\r\n");
        await tunnelTask;

        Assert.StartsWith($"CONNECT {TargetHost}:{TargetPort} HTTP/1.1\r\n", request);
        Assert.Contains($"Host: {TargetHost}:{TargetPort}\r\n", request);
        Assert.EndsWith("\r\n\r\n", request);
    }

    [Fact(Timeout = 5000)]
    public async Task Tunnel_should_succeed_on_200_response()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, TargetHost, TargetPort,
            new SimpleProxy(), null, TestContext.Current.CancellationToken);

        await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 200 Connection Established\r\n\r\n");

        await tunnelTask;
    }

    [Fact(Timeout = 5000)]
    public async Task Tunnel_should_throw_on_non_200_response()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, TargetHost, TargetPort,
            new SimpleProxy(), null, TestContext.Current.CancellationToken);

        await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => tunnelTask);
        Assert.Contains("407", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task Tunnel_should_throw_on_proxy_close()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, TargetHost, TargetPort,
            new SimpleProxy(), null, TestContext.Current.CancellationToken);

        await ReadRequestAsync(serverStream);
        serverStream.Dispose();

        await Assert.ThrowsAsync<HttpRequestException>(() => tunnelTask);
    }

    [Fact(Timeout = 5000)]
    public async Task Tunnel_should_include_proxy_auth_header()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();

        var credentials = new NetworkCredential("user", "pass");
        var proxy = new SimpleProxy(credentials);

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, TargetHost, TargetPort,
            proxy, null, TestContext.Current.CancellationToken);

        var request = await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.1 200 OK\r\n\r\n");
        await tunnelTask;

        var expectedEncoded = Convert.ToBase64String("user:pass"u8.ToArray());
        Assert.Contains($"Proxy-Authorization: Basic {expectedEncoded}\r\n", request);
    }

    [Fact(Timeout = 5000)]
    public async Task Tunnel_should_accept_http10_200_response()
    {
        var (clientStream, serverStream) = CreateDuplexPipe();

        var tunnelTask = TlsClientProvider.EstablishConnectTunnelAsync(
            clientStream, TargetHost, TargetPort,
            new SimpleProxy(), null, TestContext.Current.CancellationToken);

        await ReadRequestAsync(serverStream);
        await WriteResponseAsync(serverStream, "HTTP/1.0 200 OK\r\n\r\n");

        await tunnelTask;
    }

    private static (Stream Client, Stream Server) CreateDuplexPipe()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var clientStream = new DuplexPipeStream(
            serverToClient.Reader, clientToServer.Writer);
        var serverStream = new DuplexPipeStream(
            clientToServer.Reader, serverToClient.Writer);

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

    private sealed class SimpleProxy(ICredentials? credentials = null) : IWebProxy
    {
        public ICredentials? Credentials
        {
            get => credentials;
            set { }
        }

        public Uri? GetProxy(Uri destination) =>
            new Uri($"http://proxy.local:8080/");

        public bool IsBypassed(Uri host) => false;
    }

    private sealed class DuplexPipeStream(PipeReader reader, PipeWriter writer) : Stream
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
            sliced.CopyTo(buffer.Span);
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