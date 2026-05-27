using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Adapters;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResponseFeature : IHttpResponseFeature
{
    private readonly TurboResponseHeaderDictionary _headers = new();
    private readonly List<(Func<object?, Task> callback, object? state)> _onStartingCallbacks = [];
    private readonly List<(Func<object?, Task> callback, object? state)> _onCompletedCallbacks = [];

    public int StatusCode { get; set; } = 200;

    public string? ReasonPhrase { get; set; }

    public Stream Body { get; set; } = Stream.Null;

    public bool HasStarted { get; private set; }

    public IHeaderDictionary Headers
    {
        get => _headers;
        set { }
    }

    public void OnStarting(Func<object?, Task> callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _onStartingCallbacks.Add((callback, state));
    }

    public void OnCompleted(Func<object?, Task> callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _onCompletedCallbacks.Add((callback, state));
    }

    void IHttpResponseFeature.OnStarting(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        OnStarting((Func<object?, Task>)callback!, state!);
    }

    void IHttpResponseFeature.OnCompleted(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        OnCompleted((Func<object?, Task>)callback!, state!);
    }

    internal async Task FireOnStartingAsync()
    {
        HasStarted = true;
        foreach (var (callback, state) in _onStartingCallbacks)
        {
            await callback(state);
        }
    }

    internal async Task FireOnCompletedAsync()
    {
        foreach (var (callback, state) in _onCompletedCallbacks)
        {
            await callback(state);
        }
    }

    internal void Reset()
    {
        StatusCode = 200;
        ReasonPhrase = null;
        HasStarted = false;
        Body = Stream.Null;
        _onStartingCallbacks.Clear();
        _onCompletedCallbacks.Clear();
        _headers.Reset();
    }
}