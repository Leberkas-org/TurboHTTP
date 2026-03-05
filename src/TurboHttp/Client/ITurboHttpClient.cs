using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;

namespace TurboHttp.Client;

public record TurboClientOptions
{
    public Uri? BaseAddress { get; init; }
    public Version DefaultRequestVersion { get; init; } = HttpVersion.Version11;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxReconnectAttempts { get; init; } = 10;
    public int MaxFrameSize { get; init; } = 128 * 1024;

    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; } =
        static (_, _, _, sslPolicyErrors) => sslPolicyErrors is SslPolicyErrors.None;
    public X509CertificateCollection? ClientCertificates { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;
}

public interface ITurboHttpClient
{
    Uri? BaseAddress { get; set; }
    HttpRequestHeaders DefaultRequestHeaders { get; }
    Version DefaultRequestVersion { get; set; }
    HttpVersionPolicy DefaultVersionPolicy { get; set; }
    TimeSpan Timeout { get; set; }
    long MaxResponseContentBufferSize { get; set; }
    ChannelWriter<HttpRequestMessage> Requests { get; }
    ChannelReader<HttpResponseMessage> Responses { get; }
    void CancelPendingRequests();
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

public sealed class TurboHttpClient : ITurboHttpClient
{
    private readonly HttpRequestMessage _defaultHeadersHolder = new();
    private readonly TurboClientStreamManager _manager;

    private readonly ConcurrentDictionary<HttpRequestMessage, TaskCompletionSource<HttpResponseMessage>> _pending =
        new();

    private readonly CancellationTokenSource _cts = new();

    public Uri? BaseAddress { get; set; }
    public HttpRequestHeaders DefaultRequestHeaders => _defaultHeadersHolder.Headers;
    public Version DefaultRequestVersion { get; set; } = HttpVersion.Version11;
    public HttpVersionPolicy DefaultVersionPolicy { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
    public long MaxResponseContentBufferSize { get; set; }
    public ChannelWriter<HttpRequestMessage> Requests => _manager.Requests;
    public ChannelReader<HttpResponseMessage> Responses => _manager.Responses;

    internal TurboClientStreamManager Manager => _manager;

    public TurboHttpClient(TurboClientOptions clientOptions, ActorSystem system)
    {
        _manager = new TurboClientStreamManager(clientOptions, system, DefaultRequestHeaders);
        _ = DrainResponsesAsync(_manager.Responses, _cts.Token);
    }

    private async Task DrainResponsesAsync(
        ChannelReader<HttpResponseMessage> reader,
        CancellationToken ct)
    {
        await foreach (var response in reader.ReadAllAsync(ct))
        {
            if (response.RequestMessage is not null &&
                _pending.TryRemove(response.RequestMessage, out var tcs))
            {
                tcs.TrySetResult(response);
            }
        }
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pending.TryAdd(request, tcs);

        await _manager.Requests.WriteAsync(request, cancellationToken);

        return await tcs.Task.WaitAsync(Timeout, cancellationToken);
    }

    public void CancelPendingRequests()
    {
        foreach (var (_, tcs) in _pending)
        {
            tcs.TrySetCanceled();
        }

        _pending.Clear();
    }
}

public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient();
}