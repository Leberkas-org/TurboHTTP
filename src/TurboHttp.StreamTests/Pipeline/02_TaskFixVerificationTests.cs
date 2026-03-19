using System.IO;
using System.Net;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Pipeline;

/// <summary>
/// TASK-010: Comprehensive tests verifying all fixes from TASK-001 through TASK-009.
/// Tests focus on cross-cutting behaviors, fix correctness, and edge cases
/// not covered by individual stage test files.
/// </summary>
public sealed class TaskFixVerificationTests : StreamTestBase
{
    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-001: RetryStage — Per-Request Attempt Tracking
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 10_000,
        DisplayName = "TASK001-VFY-001: Retry attempt count stored in HttpRequestMessage.Options")]
    public async Task T001_VFY_001_RetryAttemptCount_StoredInOptions()
    {
        // After a retry, the original request's Options should contain the updated attempt count.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };

        var (_, retry) = RunRetry(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.True(retryRequest.Options.TryGetValue(
            new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), out var count));
        Assert.Equal(2, count); // attempt 1 → incremented to 2
    }

    [Fact(DisplayName = "TASK001-VFY-002: Fresh request has no attempt count in Options (defaults to 1)")]
    public void T001_VFY_002_FreshRequest_NoAttemptCountInOptions()
    {
        // A new request should NOT have the attempt count key in Options (stage treats as attempt 1).
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/new");
        Assert.False(request.Options.TryGetValue(
            new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), out _));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK001-VFY-003: Pre-seeded attempt count 2 on MaxRetries=3 still retries")]
    public async Task T001_VFY_003_PreSeededAttemptCount_StillRetries()
    {
        var policy = new RetryPolicy { MaxRetries = 3 };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Options.Set(new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), 2);
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };

        var (_, retry) = RunRetry(new RetryStage(policy), 1, response);

        // attempt 2 < MaxRetries 3 → should retry
        var retryRequest = retry.ExpectNext();
        Assert.Same(request, retryRequest);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK001-VFY-004: Pre-seeded attempt count at MaxRetries is forwarded as final")]
    public async Task T001_VFY_004_PreSeededAttemptCount_AtMax_ForwardedAsFinal()
    {
        var policy = new RetryPolicy { MaxRetries = 3 };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Options.Set(new HttpRequestOptionsKey<int>("TurboHttp.RetryAttemptCount"), 3);
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };

        var (final, retry) = RunRetry(new RetryStage(policy), 1, response);

        // attempt 3 >= MaxRetries 3 → final
        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-002: RedirectStage — Per-Request State + Version Preservation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "TASK002-VFY-001: RedirectHandler.BuildRedirectRequest preserves HTTP/2 Version")]
    public void T002_VFY_001_RedirectHandler_PreservesHttp2Version()
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
    public void T002_VFY_002_RedirectHandler_PreservesHttp10Version()
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
    public async Task T002_VFY_003_RedirectHandler_ReusesSameInstanceAcrossChain()
    {
        // First redirect creates a handler and stores it in Options.
        // When the redirect request comes back as a response (second redirect), the SAME handler
        // should be retrieved from Options, preserving the redirect count.
        var response1 = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a")
        };
        response1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");

        var (_, redirect) = RunRedirect(new RedirectStage(), 2, response1);

        var newReq1 = await redirect.ExpectNextAsync();

        // Verify handler was set
        Assert.True(newReq1.Options.TryGetValue(RedirectStage.RedirectHandlerKey, out var handler));
        Assert.Equal(1, handler!.RedirectCount);

        // Now simulate second redirect from the new request
        var response2 = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = newReq1
        };
        response2.Headers.TryAddWithoutValidation("Location", "http://example.com/c");

        // Feed response2 through a new stage (simulating pipeline re-entry)
        var (_, redirect2) = RunRedirect(new RedirectStage(), 1, response2);
        var newReq2 = await redirect2.ExpectNextAsync();

        Assert.True(newReq2.Options.TryGetValue(RedirectStage.RedirectHandlerKey, out var handler2));
        Assert.Equal(2, handler2!.RedirectCount); // same handler, count incremented
    }

    [Fact(DisplayName = "TASK002-VFY-004: Cross-scheme redirect preserves Version")]
    public void T002_VFY_004_CrossSchemeRedirect_PreservesVersion()
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

    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-003: CookieJar Thread-Safety
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 10_000,
        DisplayName = "TASK003-VFY-001: Concurrent ProcessResponse and AddCookiesToRequest do not throw")]
    public async Task T003_VFY_001_ConcurrentReadWrite_DoesNotThrow()
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
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }
        });

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
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }
        });

        // Let them run for a short time
        await Task.Delay(500);
        await cts.CancelAsync();
        await Task.WhenAll(writer, reader);

        Assert.Empty(exceptions);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK003-VFY-002: CookieJar.Count is thread-safe")]
    public async Task T003_VFY_002_CountProperty_ThreadSafe()
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
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Empty(exceptions);
        Assert.True(jar.Count > 0);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK003-VFY-003: CookieJar.Clear is thread-safe")]
    public async Task T003_VFY_003_Clear_ThreadSafe()
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
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }
        });

        var clearer = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    jar.Clear();
                    await Task.Delay(5, cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }
        });

        await Task.WhenAll(writer, clearer);
        Assert.Empty(exceptions);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-004: ConnectionReuseStage — RequestEndpoint Extraction
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 10_000,
        DisplayName = "TASK004-VFY-001: ConnectionReuseItem Key matches RequestEndpoint.FromRequest")]
    public async Task T004_VFY_001_Key_Matches_FromRequest()
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

    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-005: Http2Frame.Endpoint Does Not Affect Serialization
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "TASK005-VFY-001: Http2Frame.Endpoint does not change SerializedSize")]
    public void T005_VFY_001_Endpoint_DoesNotAffect_SerializedSize()
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
    public void T005_VFY_002_Endpoint_DoesNotAffect_WriteTo()
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
    public async Task T005_VFY_003_Request2FrameStage_SetsEndpoint_OnFirstFrame()
    {
        var encoder = new Http2RequestEncoder();
        var stage = new Request2FrameStage(encoder);

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

    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-006: Http20ConnectionStage — Endpoint From Pipeline
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 10_000,
        DisplayName = "TASK006-VFY-001: Two HEADERS frames → both StreamAcquireItems have same captured endpoint")]
    public async Task T006_VFY_001_TwoHeaders_BothAcquireItems_SameEndpoint()
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

    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-007: Http20EncoderStage — Endpoint From Pipeline
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 10_000,
        DisplayName = "TASK007-VFY-001: Frame without Endpoint before any tagged frame → Key is default")]
    public async Task T007_VFY_001_FrameWithoutEndpoint_KeyIsDefault()
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

    // ═══════════════════════════════════════════════════════════════════════════
    // TASK-008: CacheStorageStage — Async Body Reading
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 10_000,
        DisplayName = "TASK008-VFY-001: StreamContent body stored via async path")]
    public async Task T008_VFY_001_StreamContent_StoredViaAsyncPath()
    {
        var store = new HttpCacheStore();
        var bodyBytes = "async-content-test"u8.ToArray();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/async-test");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StreamContent(new MemoryStream(bodyBytes))
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        var results = await Source.Single(response)
            .Via(new CacheStorageStage(store))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(results);
        var entry = store.Get(request);
        Assert.NotNull(entry);
        Assert.Equal(bodyBytes, entry.Body);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "TASK008-VFY-002: Multiple responses cached — sync and async paths both work")]
    public async Task T008_VFY_002_MultipleResponses_BothPathsWork()
    {
        var store = new HttpCacheStore();

        // Sync path: ByteArrayContent
        var syncRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/sync");
        var syncResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = syncRequest,
            Content = new ByteArrayContent("sync"u8.ToArray())
        };
        syncResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        syncResponse.Headers.Date = DateTimeOffset.UtcNow;

        // Async path: StreamContent
        var asyncRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/async");
        var asyncResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = asyncRequest,
            Content = new StreamContent(new MemoryStream("async"u8.ToArray()))
        };
        asyncResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        asyncResponse.Headers.Date = DateTimeOffset.UtcNow;

        var results = await Source.From(new[] { syncResponse, asyncResponse })
            .Via(new CacheStorageStage(store))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Equal(2, results.Count);
        Assert.NotNull(store.Get(syncRequest));
        Assert.NotNull(store.Get(asyncRequest));
        Assert.Equal("sync"u8.ToArray(), store.Get(syncRequest)!.Body);
        Assert.Equal("async"u8.ToArray(), store.Get(asyncRequest)!.Body);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CROSS-TASK INTEGRATION TESTS
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 10_000,
        DisplayName = "CROSS-VFY-001: Retry then redirect — independent state tracking")]
    public async Task Cross_VFY_001_RetryThenRedirect_IndependentState()
    {
        // A response that is NOT retryable (200 OK) but IS a redirect (301)
        // should pass through RetryStage to final, then RedirectStage picks it up.
        // This verifies RetryStage and RedirectStage operate independently.
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old")
        {
            Version = new Version(1, 1)
        };
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
        {
            RequestMessage = original
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");

        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRedirect = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var retry = b.Add(new RetryStage());
            var merge = b.Add(new Merge<HttpResponseMessage>(2));
            var redirect = b.Add(new RedirectStage());
            var src = b.Add(Source.Single(response).Concat(Source.Never<HttpResponseMessage>()));
            var empty = b.Add(Source.Never<HttpResponseMessage>());

            b.From(src).To(retry.In);
            b.From(retry.Out0).To(merge.In(0));
            b.From(empty).To(merge.In(1));
            b.From(merge.Out).To(redirect.In);
            b.From(redirect.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(redirect.Out1).To(Sink.FromSubscriber(probeRedirect));
            b.From(retry.Out1).To(Sink.Ignore<HttpRequestMessage>().MapMaterializedValue(_ => NotUsed.Instance));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRedirect = probeRedirect.ExpectSubscription();
        subFinal.Request(1);
        subRedirect.Request(1);

        // 301 is not retryable → passes through RetryStage → picked up by RedirectStage
        var redirectRequest = await probeRedirect.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("http://example.com/new", redirectRequest.RequestUri?.AbsoluteUri);
        Assert.Equal(new Version(1, 1), redirectRequest.Version); // Version preserved (TASK-002)
        await probeFinal.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "CROSS-VFY-002: CookieStorage then CacheStorage — both run on same response")]
    public async Task Cross_VFY_002_CookieStorage_Then_CacheStorage()
    {
        var jar = new CookieJar();
        var store = new HttpCacheStore();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://cross.example.com/test"),
            Content = new ByteArrayContent("cross-test"u8.ToArray())
        };
        response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=abc; Domain=cross.example.com; Path=/");
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        await Source.Single(response)
            .Via(new CookieStorageStage(jar))
            .Via(new CacheStorageStage(store))
            .RunWith(Sink.Ignore<HttpResponseMessage>(), Materializer);

        // Cookie stored (TASK-003)
        Assert.Equal(1, jar.Count);

        // Response cached (TASK-008)
        var cached = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://cross.example.com/test"));
        Assert.NotNull(cached);
        Assert.Equal("cross-test"u8.ToArray(), cached.Body);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "CROSS-VFY-003: Http2Frame Endpoint survives encode round-trip")]
    public async Task Cross_VFY_003_Http2Frame_Endpoint_SurvivesEncodeRoundTrip()
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

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private (TestSubscriber.ManualProbe<HttpResponseMessage> final,
        TestSubscriber.ManualProbe<HttpRequestMessage> retry) RunRetry(
            RetryStage stage,
            int demandEach,
            params HttpResponseMessage[] responses)
    {
        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRetry = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var src = b.Add(Source.From(responses).Concat(Source.Never<HttpResponseMessage>()));

            b.From(src).To(s.In);
            b.From(s.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(s.Out1).To(Sink.FromSubscriber(probeRetry));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRetry = probeRetry.ExpectSubscription();
        subFinal.Request(demandEach);
        subRetry.Request(demandEach);

        return (probeFinal, probeRetry);
    }

    private (TestSubscriber.ManualProbe<HttpResponseMessage> final,
        TestSubscriber.ManualProbe<HttpRequestMessage> redirect) RunRedirect(
            RedirectStage stage,
            int demandEach,
            params HttpResponseMessage[] responses)
    {
        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRedirect = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var src = b.Add(Source.From(responses).Concat(Source.Never<HttpResponseMessage>()));

            b.From(src).To(s.In);
            b.From(s.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(s.Out1).To(Sink.FromSubscriber(probeRedirect));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRedirect = probeRedirect.ExpectSubscription();
        subFinal.Request(demandEach);
        subRedirect.Request(demandEach);

        return (probeFinal, probeRedirect);
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

                    b.From(serverSource).To(stage.ServerIn);
                    b.From(stage.AppOut).To(downstreamSink);
                    b.From(requestSource).To(stage.AppIn);
                    b.From(stage.ServerOut).To(sbSink);
                    b.From(stage.OutletSignal).To(sigSink);

                    return ClosedShape.Instance;
                }));

        var (serverBoundTask, signalTask) = graph.Run(Materializer);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));
        var signals = await signalTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (serverBound, signals);
    }
}
