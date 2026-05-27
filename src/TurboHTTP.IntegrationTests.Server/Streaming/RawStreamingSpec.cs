using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Streaming;

public sealed class RawStreamingSpec : ServerSpecBase
{
    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/stream-bytes", () =>
        {
            var chunks = new[]
            {
                (ReadOnlyMemory<byte>)new byte[] { 1, 2, 3 },
                (ReadOnlyMemory<byte>)new byte[] { 4, 5, 6 },
                (ReadOnlyMemory<byte>)new byte[] { 7, 8, 9 }
            };
            return TurboStreamResults.Stream(Source.From(chunks), "application/octet-stream");
        });

        routeTable.Add("GET", "/stream-text", () =>
        {
            var lines = new[] { "line1\n", "line2\n", "line3\n" };
            var chunks = lines.Select(l =>
                (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l)).ToArray();
            return TurboStreamResults.Stream(Source.From(chunks), "text/plain");
        });

        routeTable.Add("GET", "/stream-large", () =>
        {
            var chunk = new byte[1024];
            Array.Fill(chunk, (byte)0xAB);
            var source = Source.From(Enumerable.Range(0, 100)
                .Select(_ => (ReadOnlyMemory<byte>)chunk.ToArray().AsMemory()));
            return TurboStreamResults.Stream(source, "application/octet-stream");
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Stream_should_return_all_bytes()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-bytes"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes);
    }

    [Fact(Timeout = 15000)]
    public async Task Stream_should_set_custom_content_type()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("line1\nline2\nline3\n", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Stream_should_handle_large_payload()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-large"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(100 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal(0xAB, b));
    }
}
