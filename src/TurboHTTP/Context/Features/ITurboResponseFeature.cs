using TurboHTTP.Context;

namespace TurboHTTP.Context.Features;

public interface ITurboResponseFeature
{
    int StatusCode { get; set; }
    string? ReasonPhrase { get; set; }
    ITurboHeaderDictionary Headers { get; }
    Stream Body { get; set; }
    bool HasStarted { get; }
    void OnStarting(Func<object?, Task> callback, object? state);
    void OnCompleted(Func<object?, Task> callback, object? state);
}
