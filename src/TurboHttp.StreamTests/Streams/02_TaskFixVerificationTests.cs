using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Features;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Regression tests that verify fixes for void-related bugs in stage interaction.
/// Guards against re-introduction of known concurrency bugs caused by improper Task cancellation handling.
/// </summary>
/// <remarks>
/// Stage under test: various pipeline stages (BidiFlow architecture).
/// Ensures specific concurrency edge cases that were previously broken remain correctly handled.
/// </remarks>
public sealed class TaskFixVerificationTests : StreamTestBase
{
    [Fact(DisplayName = "TASK001-VFY-001: Retry attempt count stored in HttpRequestMessage.Options")]
    public void Should_StoreRetryAttemptCountInOptions_When_RetryOccurs()
    {
        // After a retry, the original request's Options should contain the updated attempt count.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };

        var (reqOut, _, pushResp, _) = RunRetryBidi(new RetryBidiStage(new RetryPolicy()), 5, 5, request);

        // Original request forwarded on Out1
        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // Push retryable response on In2
        pushResp(response);

        // Retry request appears on Out1 with updated attempt count
        var retryRequest = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.True(retryRequest.Options.TryGetValue(
            new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), out var count));
        Assert.Equal(2, count); // attempt 1 → incremented to 2
    }

    [Fact(DisplayName = "TASK001-VFY-002: Fresh request has no attempt count in Options (defaults to 1)")]
    public void Should_HaveNoAttemptCountInOptions_When_RequestIsFresh()
    {
        // A new request should NOT have the attempt count key in Options (stage treats as attempt 1).
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/new");
        Assert.False(request.Options.TryGetValue(
            new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), out _));
    }

    [Fact(DisplayName = "TASK001-VFY-003: Pre-seeded attempt count 2 on MaxRetries=3 still retries")]
    public void Should_StillRetry_When_PreSeededAttemptCountBelowMaxRetries()
    {
        var policy = new RetryPolicy { MaxRetries = 3 };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Options.Set(new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), 2);
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };

        var (reqOut, _, pushResp, _) = RunRetryBidi(new RetryBidiStage(policy), 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken); // original forwarded
        pushResp(response);

        // attempt 2 < MaxRetries 3 → should retry
        var retryRequest = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryRequest);
    }

    [Fact(DisplayName = "TASK001-VFY-004: Pre-seeded attempt count at MaxRetries is forwarded as final")]
    public void Should_ForwardAsFinal_When_PreSeededAttemptCountAtMaxRetries()
    {
        var policy = new RetryPolicy { MaxRetries = 3 };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Options.Set(new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), 3);
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };

        var (reqOut, respOut, pushResp, _) = RunRetryBidi(new RetryBidiStage(policy), 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken); // original forwarded
        pushResp(response);

        // attempt 3 >= MaxRetries 3 → final response on Out2
        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
        reqOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "TASK002-VFY-001: RedirectHandler.BuildRedirectRequest preserves HTTP/2 Version")]
    public void Should_PreserveHttp2Version_When_BuildingRedirectRequest()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old")
        {
            Version = new Version(2, 0)
        };
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal(new Version(2, 0), newRequest.Version);
    }

    [Fact(DisplayName = "TASK002-VFY-002: RedirectHandler.BuildRedirectRequest preserves HTTP/1.0 Version")]
    public void Should_PreserveHttp10Version_When_BuildingRedirectRequest()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://legacy.com/old")
        {
            Version = new Version(1, 0)
        };
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        response.Headers.TryAddWithoutValidation("Location", "http://legacy.com/new");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal(new Version(1, 0), newRequest.Version);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK002-VFY-003: Redirect handler via Options reuses same instance across chain")]
    public async Task Should_ReuseHandlerAcrossChain_When_RedirectRequestHandledViaOptions()
    {
        // First redirect creates a handler and stores it in Options.
        // When the redirect request comes back as a response (second redirect), the SAME handler
        // should be retrieved from Options, preserving the redirect count.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, _, pushResp, _) = RunRedirectBidi(stage, 10, 10, request);

        // Original request forwarded
        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // Push 302 redirect response
        var response1 = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = request
        };
        response1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");
        pushResp(response1);

        var newReq1 = reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // Verify handler was set
        Assert.True(newReq1.Options.TryGetValue(RedirectBidiStage.RedirectHandlerKey, out var handler));
        Assert.Equal(1, handler!.RedirectCount);

        // Push second redirect response for the redirect request
        var response2 = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = newReq1
        };
        response2.Headers.TryAddWithoutValidation("Location", "http://example.com/c");
        pushResp(response2);

        var newReq2 = reqOut.ExpectNext(TestContext.Current.CancellationToken);

        Assert.True(newReq2.Options.TryGetValue(RedirectBidiStage.RedirectHandlerKey, out var handler2));
        Assert.Equal(2, handler2!.RedirectCount); // same handler, count incremented
        await Task.CompletedTask;
    }

    [Fact(DisplayName = "TASK002-VFY-004: Cross-scheme redirect preserves Version")]
    public void Should_PreserveVersion_When_CrossSchemeRedirectOccurs()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://secure.com/resource")
        {
            Version = new Version(2, 0)
        };
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.TryAddWithoutValidation("Location", "http://insecure.com/resource");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal(new Version(2, 0), newRequest.Version);
        Assert.Equal("http", newRequest.RequestUri!.Scheme);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK003-VFY-001: Concurrent ProcessResponse and AddCookiesToRequest do not throw")]
    public async Task Should_NotThrow_When_ConcurrentCookieJarReadWriteOccurs()
    {
        var jar = new CookieJar();
        var uri = new Uri("http://example.com/path");

        // Pre-seed with some cookies
        for (var i = 0; i < 10; i++)
        {
            var seedResponse = new HttpResponseMessage();
            seedResponse.Headers.TryAddWithoutValidation("Set-Cookie",
                $"cookie{i}=value{i}; Domain=example.com; Path=/");
            jar.ProcessResponse(uri, seedResponse);
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var exceptions = new List<Exception>();

        // Writer task: continuously store cookies
        var writer = Task.Run(async () =>
        {
            var counter = 100;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var resp = new HttpResponseMessage();
                    resp.Headers.TryAddWithoutValidation("Set-Cookie",
                        $"dyn{counter++}=val; Domain=example.com; Path=/");
                    jar.ProcessResponse(uri, resp);
                    await Task.Delay(1, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        // Reader task: continuously read cookies
        var reader = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
                    jar.AddCookiesToRequest(uri, ref req);
                    await Task.Delay(1, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        // Let them run for a short time
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await Task.WhenAll(writer, reader);

        Assert.Empty(exceptions);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK003-VFY-002: CookieJar.Count is thread-safe")]
    public async Task Should_BeThreadSafe_When_AccessingCookieJarCountProperty()
    {
        var jar = new CookieJar();
        var uri = new Uri("http://example.com/");
        var exceptions = new List<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var resp = new HttpResponseMessage();
                    resp.Headers.TryAddWithoutValidation("Set-Cookie",
                        $"t{i}_{Guid.NewGuid():N}=v; Domain=example.com; Path=/");
                    jar.ProcessResponse(uri, resp);
                    _ = jar.Count; // concurrent read
                    await Task.Delay(1, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Empty(exceptions);
        Assert.True(jar.Count > 0);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK003-VFY-003: CookieJar.Clear is thread-safe")]
    public async Task Should_BeThreadSafe_When_CallingCookieJarClear()
    {
        var jar = new CookieJar();
        var uri = new Uri("http://example.com/");
        var exceptions = new List<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writer = Task.Run(async () =>
        {
            var counter = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var resp = new HttpResponseMessage();
                    resp.Headers.TryAddWithoutValidation("Set-Cookie",
                        $"c{counter++}=v; Domain=example.com; Path=/");
                    jar.ProcessResponse(uri, resp);
                    await Task.Delay(1, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        var clearer = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    jar.Clear();
                    await Task.Delay(5, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(writer, clearer);
        Assert.Empty(exceptions);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK004-VFY-001: ConnectionReuseItem Key matches RequestEndpoint.FromRequest")]
    public async Task Should_MatchRequestEndpointFromRequest_When_ConnectionReuseItemCreated()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test.com:9090/data")
        {
            Version = HttpVersion.Version11
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Version = HttpVersion.Version11
        };

        var expected = RequestEndpoint.FromRequest(request);

        var probe0 = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probe1 = this.CreateManualSubscriberProbe<IOutputItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ConnectionReuseStage());
            var src = b.Add(Source.Single(response));

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);
        sub0.Request(1);
        sub1.Request(1);

        var signal = (ConnectionReuseItem)await probe1.ExpectNextAsync(CancellationToken.None);
        Assert.Equal(expected, signal.Key);
    }

    [Fact(DisplayName = "TASK005-VFY-001: Http2Frame.Endpoint does not change SerializedSize")]
    public void Should_NotAffectSerializedSize_When_Http2FrameHasEndpoint()
    {
        var frameWithout = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: true);
        var frameWith = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: true)
        {
            Endpoint = new RequestEndpoint
            {
                Scheme = "https",
                Host = "example.com",
                Port = 443,
                Version = HttpVersion.Version20
            }
        };

        Assert.Equal(frameWithout.SerializedSize, frameWith.SerializedSize);
    }

    [Fact(DisplayName = "TASK005-VFY-002: Http2Frame.Endpoint does not change WriteTo output")]
    public void Should_NotAffectWriteToOutput_When_Http2FrameHasEndpoint()
    {
        var block = new byte[] { 0x82, 0x84, 0x86 };
        var frameWithout = new HeadersFrame(streamId: 3, headerBlock: block, endStream: false);
        var frameWith = new HeadersFrame(streamId: 3, headerBlock: block, endStream: false)
        {
            Endpoint = new RequestEndpoint
            {
                Scheme = "https",
                Host = "api.example.com",
                Port = 8443,
                Version = HttpVersion.Version20
            }
        };

        var bytesWithout = frameWithout.Serialize();
        var bytesWith = frameWith.Serialize();

        Assert.Equal(bytesWithout, bytesWith);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK005-VFY-003: Request2FrameStage sets Endpoint on first frame only")]
    public async Task Should_SetEndpointOnFirstFrameOnly_When_Request2FrameStageProcessesRequest()
    {
        var encoder = new Http2RequestEncoder();
        var stage = new Http20Request2FrameStage(encoder);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
        {
            Version = HttpVersion.Version20
        };

        var frames = await Source.Single((request, 1))
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);

        Assert.NotEmpty(frames);

        // First frame should have Endpoint set
        Assert.NotNull(frames[0].Endpoint);
        Assert.Equal("example.com", frames[0].Endpoint!.Value.Host);

        // Subsequent frames (if any) should NOT have Endpoint set
        foreach (var frame in frames.Skip(1))
        {
            Assert.Null(frame.Endpoint);
        }
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK006-VFY-001: Two HEADERS frames → both StreamAcquireItems have same captured endpoint")]
    public async Task Should_HaveSameEndpointOnBothAcquireItems_When_TwoHeaderFramesSent()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "multi.example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var h1 = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endHeaders: true, endStream: true)
        {
            Endpoint = endpoint
        };
        var h2 = new HeadersFrame(streamId: 3, headerBlock: new byte[] { 0x82 }, endHeaders: true, endStream: true);
        // h2 has no Endpoint — stage should reuse captured one

        var (_, signals) = await RunConnectionStageWithRequests(h1, h2);

        Assert.Equal(2, signals.Count);
        var a1 = Assert.IsType<StreamAcquireItem>(signals[0]);
        var a2 = Assert.IsType<StreamAcquireItem>(signals[1]);
        Assert.Equal(endpoint, a1.Key);
        Assert.Equal(endpoint, a2.Key);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK007-VFY-001: Frame without Endpoint before any tagged frame → Key is default")]
    public async Task Should_HaveDefaultKey_When_Http2FrameHasNoEndpoint()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0x01 }, endStream: true);
        // No Endpoint set on any frame

        var items = await Source.Single<Http2Frame>(frame)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var dataItem = Assert.Single(items);
        Assert.IsType<DataItem>(dataItem);
        Assert.Equal(default(RequestEndpoint), ((DataItem)dataItem).Key);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK008-VFY-001: StreamContent body stored via async path")]
    public async Task Should_StoreBodyViaAsyncPath_When_ContentIsStreamContent()
    {
        var store = new CacheStore();
        var bodyBytes = "async-content-test"u8.ToArray();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/async-test");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StreamContent(new MemoryStream(bodyBytes))
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        var (reqOut, respOut, pushResp, _) = RunCacheBidi(store, request);

        // Request forwarded (cache miss on empty store)
        await reqOut.ExpectNextAsync(CancellationToken.None);

        // Push response on In2 — triggers cache storage
        pushResp(response);

        // Response appears on Out2 after being stored
        var result = await respOut.ExpectNextAsync(CancellationToken.None);
        Assert.Same(response, result);

        // Allow async callback to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var entry = store.Get(request);
        Assert.NotNull(entry);
        Assert.Equal(bodyBytes, entry.Body);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK008-VFY-002: Multiple responses cached — sync and async paths both work")]
    public async Task Should_CacheBothSyncAndAsyncResponses_When_MultipleResponsesProcessed()
    {
        var store = new CacheStore();

        // Sync path: ByteArrayContent
        var syncRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/sync");
        var syncResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = syncRequest,
            Content = new ByteArrayContent("sync"u8.ToArray())
        };
        syncResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        syncResponse.Headers.Date = DateTimeOffset.UtcNow;

        // First request/response through BidiStage
        var (reqOut1, respOut1, pushResp1, _) = RunCacheBidi(store, syncRequest);
        await reqOut1.ExpectNextAsync(CancellationToken.None);
        pushResp1(syncResponse);
        await respOut1.ExpectNextAsync(CancellationToken.None);

        // Async path: StreamContent
        var asyncRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/async");
        var asyncResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = asyncRequest,
            Content = new StreamContent(new MemoryStream("async"u8.ToArray()))
        };
        asyncResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        asyncResponse.Headers.Date = DateTimeOffset.UtcNow;

        // Second request/response through a fresh BidiStage with same store
        var (reqOut2, respOut2, pushResp2, _) = RunCacheBidi(store, asyncRequest);
        await reqOut2.ExpectNextAsync(CancellationToken.None);
        pushResp2(asyncResponse);
        await respOut2.ExpectNextAsync(CancellationToken.None);

        // Allow async callback to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.NotNull(store.Get(syncRequest));
        Assert.NotNull(store.Get(asyncRequest));
        Assert.Equal("sync"u8.ToArray(), store.Get(syncRequest)!.Body);
        Assert.Equal("async"u8.ToArray(), store.Get(asyncRequest)!.Body);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "CROSS-VFY-001: Retry then redirect via BidiFlow.Atop — independent state tracking")]
    public async Task Should_TrackStateIndependently_When_RetryAndRedirectStacked()
    {
        // A response that is NOT retryable but IS a redirect (301)
        // should pass through RetryBidiStage unchanged, then RedirectBidiStage handles it.
        // This verifies the BidiFlow.Atop stacking operates independently.
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old")
        {
            Version = new Version(1, 1)
        };

        var retryBidi = new RetryBidiStage();
        var redirectBidi = new RedirectBidiStage(new RedirectPolicy());

        // Stack: redirect atop retry (outermost → innermost)
        var stacked = BidiFlow.FromGraph(redirectBidi).Atop(BidiFlow.FromGraph(retryBidi));

        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stacked);
            var reqSrc = b.Add(Source.Single(original).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var respSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        reqOutSub.Request(10);
        respOutSub.Request(10);

        // Original request forwarded through both stages
        requestOutProbe.ExpectNext(TestContext.Current.CancellationToken);

        // Push 301 redirect response — not retryable, but redirectable
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
        {
            RequestMessage = original
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");
        respSub.SendNext(response);

        // RedirectBidiStage should produce a redirect request on Out1
        var redirectRequest = requestOutProbe.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal("http://example.com/new", redirectRequest.RequestUri?.AbsoluteUri);
        Assert.Equal(new Version(1, 1), redirectRequest.Version); // Version preserved (TASK-002)
        responseOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 10_000,
        DisplayName = "CROSS-VFY-002: CookieBidi then CacheBidi — both run on same response via Atop")]
    public async Task Should_StoreCookieAndCache_When_CookieBidiAndCacheBidiStacked()
    {
        var jar = new CookieJar();
        var store = new CacheStore();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://cross.example.com/test");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent("cross-test"u8.ToArray())
        };
        response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=abc; Domain=cross.example.com; Path=/");
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        // Stack: cookie atop cache (cookie processes first on response path)
        var cookieBidi = new CookieBidiStage(jar);
        var cacheBidi = new CacheBidiStage(store);
        var stacked = BidiFlow.FromGraph(cookieBidi).Atop(BidiFlow.FromGraph(cacheBidi));

        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stacked);
            var reqSrc = b.Add(Source.Single(request).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var respSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(10);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(10);

        // Request forwarded through both stages (cache miss)
        await requestOutProbe.ExpectNextAsync(CancellationToken.None);

        // Push response on In2 — flows through cache storage then cookie storage
        respSub.SendNext(response);

        var result = await responseOutProbe.ExpectNextAsync(CancellationToken.None);
        Assert.Same(response, result);

        // Cookie stored (TASK-003)
        Assert.Equal(1, jar.Count);

        // Response cached (TASK-008)
        var cached = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://cross.example.com/test"));
        Assert.NotNull(cached);
        Assert.Equal("cross-test"u8.ToArray(), cached.Body);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "CROSS-VFY-003: Http2Frame Endpoint survives encode round-trip")]
    public async Task Should_PreserveEndpointAsKey_When_Http2FrameEncodedWithEndpoint()
    {
        // The Endpoint property is metadata — it should be set on the frame
        // before encoding and the encoded bytes should NOT be affected by it.
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "roundtrip.example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var frame = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: true)
        {
            Endpoint = endpoint
        };

        // Encode via stage
        var items = await Source.Single<Http2Frame>(frame)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var dataItem = (DataItem)Assert.Single(items);

        // Key should be captured from Endpoint
        Assert.Equal(endpoint, dataItem.Key);

        // Encoded bytes should be standard HTTP/2 frame (9-byte header + payload)
        var bytes = dataItem.Memory.Memory.Span[..dataItem.Length].ToArray();
        Assert.True(bytes.Length >= 9);
        Assert.Equal(0x01, bytes[3]); // HEADERS frame type
        dataItem.Memory.Dispose();
    }

    /// <summary>
    /// Materialises a <see cref="RetryBidiStage"/> with manual publisher/subscriber probes.
    /// Sends the given requests on In1 (concat with Never to prevent completion).
    /// Returns probes for Out1 (request output, including retries), Out2 (final responses),
    /// and a push function for In2 (response input).
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse,
        Action completeResponse) RunRetryBidi(
            RetryBidiStage stage,
            int requestOutDemand,
            int responseOutDemand,
            params HttpRequestMessage[] requests)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var respSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(requestOutDemand);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(responseOutDemand);

        return (requestOutProbe, responseOutProbe, respSub.SendNext, respSub.SendComplete);
    }

    /// <summary>
    /// Materialises a <see cref="RedirectBidiStage"/> with manual publisher/subscriber probes.
    /// Sends the given requests on In1 (concat with Never to prevent completion).
    /// Returns probes for Out1 (request output, including redirects), Out2 (final responses),
    /// and a push function for In2 (response input).
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse,
        Action completeResponse) RunRedirectBidi(
            RedirectBidiStage stage,
            int requestOutDemand,
            int responseOutDemand,
            params HttpRequestMessage[] requests)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var respSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(requestOutDemand);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(responseOutDemand);

        return (requestOutProbe, responseOutProbe, respSub.SendNext, respSub.SendComplete);
    }

    /// <summary>
    /// Materialises a <see cref="CacheBidiStage"/> with manual publisher/subscriber probes.
    /// Sends a single request on In1 (concat with Never to prevent completion).
    /// Returns probes for Out1, Out2, and a push function for In2.
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse,
        Action completeResponse) RunCacheBidi(
            CacheStore store,
            params HttpRequestMessage[] requests)
    {
        var stage = new CacheBidiStage(store);
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var respSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(10);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(10);

        return (requestOutProbe, responseOutProbe, respSub.SendNext, respSub.SendComplete);
    }

    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)>
        RunConnectionStageWithRequests(params Http2Frame[] requestFrames)
    {
        var serverBoundSink = Sink.Seq<Http2Frame>();
        var signalSeqSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(serverBoundSink, signalSeqSink,
                (m1, m2) => (m1, m2),
                (b, sbSink, sigSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(
                        Source.Single<Http2Frame>(new SettingsFrame([], isAck: true))
                            .InitialDelay(TimeSpan.FromMilliseconds(200)));
                    var requestSource = b.Add(Source.From(requestFrames));
                    var downstreamSink = b.Add(Sink.Ignore<Http2Frame>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutStream).To(downstreamSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(sigSink);

                    return ClosedShape.Instance;
                }));

        var (serverBoundTask, signalTask) = graph.Run(Materializer);
        var serverBound =
            await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var signals = await signalTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (serverBound, signals);
    }
}