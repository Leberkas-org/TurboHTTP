using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Streams;
using TurboHttp.Streams.Lifecycle;

namespace TurboHttp;

/// <summary>
/// Snapshot of <see cref="TurboHttpClient"/> configuration captured at request-submission time.
/// Passed into the pipeline so that per-request options reflect the values set on the client at the moment of submission.
/// </summary>
public record TurboRequestOptions(
    Uri? BaseAddress,
    HttpRequestHeaders DefaultRequestHeaders,
    Version DefaultRequestVersion,
    HttpVersionPolicy DefaultVersionPolicy,
    TimeSpan Timeout,
    long MaxResponseContentBufferSize);

public sealed class TurboHttpClient : ITurboHttpClient
{
    private readonly HttpRequestOptionsKey<Guid> _key = new("RequestId");
    private readonly HttpRequestMessage _defaultHeadersHolder = new();

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<HttpResponseMessage>> _pending = new();

    private readonly CancellationTokenSource _cts = new();

    public Uri? BaseAddress { get; set; }
    public HttpRequestHeaders DefaultRequestHeaders => _defaultHeadersHolder.Headers;
    public Version DefaultRequestVersion { get; set; } = HttpVersion.Version11;
    public HttpVersionPolicy DefaultVersionPolicy { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
    public long MaxResponseContentBufferSize { get; set; }
    public ChannelWriter<HttpRequestMessage> Requests => Manager.Requests;
    public ChannelReader<HttpResponseMessage> Responses => Manager.Responses;

    internal TurboClientStreamManager Manager { get; }

    public TurboHttpClient(TurboClientOptions clientOptions, ActorSystem system)
        : this(clientOptions, system, PipelineDescriptor.Empty)
    {
    }

    internal TurboHttpClient(TurboClientOptions clientOptions, ActorSystem system, PipelineDescriptor pipeline)
    {
        Manager = new TurboClientStreamManager(clientOptions, OptionsFactory, system, pipeline);
        _ = DrainResponsesAsync(Manager.Responses, _cts.Token);
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
        try
        {
            await foreach (var response in reader.ReadAllAsync(ct)
                               .Where(x => x.RequestMessage is not null)
                               .WithCancellation(ct))
            {
                var request = response.RequestMessage!;
                if (request.Options.TryGetValue(_key, out var requestId) &&
                    _pending.TryRemove(requestId, out var tcs))
                {
                    tcs.TrySetResult(response);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown path — CancellationToken cancelled (e.g. Dispose). Do not fail pending requests.
        }
        catch (Exception ex)
        {
            // Response channel closed with an error — fail every pending request so callers are not stuck.
            foreach (var (_, tcs) in _pending)
            {
                tcs.TrySetException(ex);
            }

            _pending.Clear();
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestId = Guid.NewGuid();
        request.Options.Set(_key, requestId);
        _pending.TryAdd(requestId, tcs);
        try
        {
            await Manager.Requests.WriteAsync(request, cancellationToken);
            return await tcs.Task.WaitAsync(Timeout, cancellationToken);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public void Dispose() => Manager.Dispose();

    public void CancelPendingRequests()
    {
        foreach (var (_, tcs) in _pending)
        {
            tcs.SetCanceled();
        }

        _pending.Clear();
    }
}