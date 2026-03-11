using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Servus.Core;

namespace TurboHttp.Client;

public record TurboRequestOptions(
    Uri? BaseAddress,
    HttpRequestHeaders DefaultRequestHeaders,
    Version DefaultRequestVersion,
    HttpVersionPolicy DefaultVersionPolicy,
    TimeSpan Timeout,
    long MaxResponseContentBufferSize);

public sealed class TurboHttpClient : ITurboHttpClient
{
    private readonly HttpRequestOptionsKey<Guid> _key = new HttpRequestOptionsKey<Guid>("RequestId");
    private readonly HttpRequestMessage _defaultHeadersHolder = new();
    private readonly TurboClientStreamManager _manager;

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<HttpResponseMessage>> _pending =
        new();

    private readonly CancellationTokenSource _cts = new();

    public Uri? BaseAddress { get; set; }
    public HttpRequestHeaders DefaultRequestHeaders => _defaultHeadersHolder.Headers;
    public Version DefaultRequestVersion { get; set; } = HttpVersion.Version11;
    public HttpVersionPolicy DefaultVersionPolicy { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
    public long MaxResponseContentBufferSize { get; set; }
    public ChannelWriter<HttpRequestMessage> Requests => _manager.Requests;
    public ChannelReader<HttpResponseMessage> Responses => _manager.Responses;

    internal TurboClientStreamManager Manager => _manager;

    public TurboHttpClient(TurboClientOptions clientOptions, ActorSystem system)
    {
        _manager = new TurboClientStreamManager(clientOptions, OptionsFactory, system);
        _ = DrainResponsesAsync(_manager.Responses, _cts.Token);
        return;

        TurboRequestOptions OptionsFactory()
            => new(BaseAddress,
                DefaultRequestHeaders,
                DefaultRequestVersion,
                DefaultVersionPolicy,
                Timeout,
                MaxResponseContentBufferSize);
    }

    private async Task DrainResponsesAsync(ChannelReader<HttpResponseMessage> reader, CancellationToken ct)
    {
        await foreach (var response in reader.ReadAllAsync(ct)
                           .Where(x => x.RequestMessage is not null)
                           .WithCancellation(ct))
        {
            var request = response.RequestMessage!;
            if (request.Options.TryGetValue(_key, out var requestId) &&
                _pending.TryGetValue(requestId, out var tcs))
            {
                tcs.TrySetResult(response);
            }
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestId = Guid.NewGuid();
        request.Options.Set(_key, requestId);
        _pending.TryAdd(requestId, tcs);
        await _manager.Requests.WriteAsync(request, cancellationToken);
        return await tcs.Task.WaitAsync(Timeout, cancellationToken);
    }

    public void CancelPendingRequests()
    {
        foreach (var (_, tcs) in _pending)
        {
            tcs.SetCanceled();
        }

        _pending.Clear();
    }
}