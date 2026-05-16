using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Diagnostics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;
using static Servus.Core.Servus;
using Activity = System.Diagnostics.Activity;
using ActivityListener = System.Diagnostics.ActivityListener;
using ActivitySamplingResult = System.Diagnostics.ActivitySamplingResult;
using ActivitySource = System.Diagnostics.ActivitySource;

namespace TurboHTTP.Tests.Protocol.Semantics.Stages;

public sealed class TracingActivityLeakSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.1")]
    public async Task TracingBidiStage_should_stop_activity_when_stage_tears_down_without_response()
    {
        var stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Activity? capturedActivity = null;

        var sourceName = Tracing.Source.Name;
        using var listener = new ActivityListener();
        listener.ShouldListenTo = source => source.Name == sourceName;
        listener.Sample = (ref _) => ActivitySamplingResult.AllData;
        // Wire ActivityStopped before AddActivityListener so the callback is always
        // registered before the Akka dispatch thread can call PostStop.
        listener.ActivityStopped = stopped =>
        {
            if (capturedActivity is { } a && ReferenceEquals(stopped, a))
            {
                stoppedTcs.TrySetResult();
            }
        };
        ActivitySource.AddActivityListener(listener);

        var stage = new TracingBidiStage();

        var reqPub = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var respPub = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var reqOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var respOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(reqPub)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(reqOutProbe));
            b.From(Source.FromPublisher(respPub)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(respOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqInSub = await reqPub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var respInSub = await respPub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var reqOutSub = await reqOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var respOutSub = await respOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        reqOutSub.Request(10);
        respOutSub.Request(10);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/tracing-leak");
        reqInSub.SendNext(request);
        var forwarded = await reqOutProbe.ExpectNextAsync(TestContext.Current.CancellationToken);

        Assert.True(forwarded.Options.TryGetValue(TurboHttpInstrumentationExtensions.RequestActivityKey,
            out var activity));
        Assert.NotNull(activity);

        capturedActivity = activity;

        // Tear down the stage — complete both inlets and cancel downstream
        reqInSub.SendComplete();
        respInSub.SendComplete();
        reqOutSub.Cancel();
        respOutSub.Cancel();

        // PostStop must stop the in-flight activity
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}