using System.Diagnostics;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

public sealed class TurboHttpDiagnosticSourceSpec : IDisposable
{
    private readonly List<KeyValuePair<string, object?>> _events = [];
    private readonly IDisposable _subscription;

    public TurboHttpDiagnosticSourceSpec()
    {
        var observer = new TestObserver(_events);
        _subscription = DiagnosticListener.AllListeners.Subscribe(new TestListenerObserver(observer));
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ListenerName_should_be_TurboHTTP()
    {
        Assert.Equal("TurboHTTP", TurboHttpDiagnosticSource.ListenerName);
    }

    [Fact(Timeout = 5000)]
    public void OnRequestStart_should_emit_event()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        TurboHttpDiagnosticSource.OnRequestStart(request);

        var evt = _events.FirstOrDefault(e => e.Key == "TurboHTTP.HttpRequestOut.Start");
        Assert.NotNull(evt.Value);
    }

    [Fact(Timeout = 5000)]
    public void OnRequestStop_should_emit_event()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        TurboHttpDiagnosticSource.OnRequestStop(request, response, TaskStatus.RanToCompletion);

        var evt = _events.FirstOrDefault(e => e.Key == "TurboHTTP.HttpRequestOut.Stop");
        Assert.NotNull(evt.Value);
    }

    [Fact(Timeout = 5000)]
    public void OnException_should_emit_event()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var exception = new HttpRequestException("Connection refused");

        TurboHttpDiagnosticSource.OnException(request, exception);

        var evt = _events.FirstOrDefault(e => e.Key == "TurboHTTP.Exception");
        Assert.NotNull(evt.Value);
    }

    private sealed class TestListenerObserver(TestObserver inner) : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == TurboHttpDiagnosticSource.ListenerName)
            {
                value.Subscribe(inner);
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private sealed class TestObserver(List<KeyValuePair<string, object?>> events)
        : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value) => events.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
