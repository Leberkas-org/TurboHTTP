using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Adapters;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResponseFeature : IHttpResponseFeature
{
    private readonly List<(Func<object?, Task> callback, object? state)> _onStartingCallbacks = [];
    private readonly List<(Func<object?, Task> callback, object? state)> _onCompletedCallbacks = [];

    public int StatusCode { get; set; } = 200;

    public string? ReasonPhrase { get; set; }

    public IHeaderDictionary Headers { get; set; } = new TurboResponseHeaderDictionary();

    public Stream Body { get; set; } = Stream.Null;

    public bool HasStarted { get; private set; }

    public void OnStarting(Func<object, Task> callback, object state)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        _onStartingCallbacks.Add((callback, state)!);
    }

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        _onCompletedCallbacks.Add((callback, state)!);
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
}