using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Internal;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams;

/// <summary>
/// Tests the 6-slot fan-out behavior of <see cref="GroupByRequestEndpointStage{T}"/>,
/// including per-key slot limits, connection affinity tagging, slot reuse after completion,
/// and least-loaded routing once all slots are saturated.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="GroupByRequestEndpointStage{T}"/>.
/// Validates maxSubstreamsPerKey = 6, connection affinity re-injection, and load distribution.
/// </remarks>
public sealed class GroupByEndpointFanOutSpec : StreamTestBase
{
    // Helpers

    private static HttpRequestMessage Req(string url)
        => new(HttpMethod.Get, url) { Version = HttpVersion.Version11 };

    /// <summary>
    /// Returns the <see cref="GroupByRequestEndpointStage{T}.ConnectionAffinitySlot"/> ID
    /// stamped on a request by the stage, or -1 if the option is absent.
    /// </summary>
    private static int SlotOf(HttpRequestMessage req)
        => req.Options.TryGetValue(
            GroupByRequestEndpointStage<HttpRequestMessage>.ConnectionAffinitySlot,
            out var id)
            ? id
            : -1;

    /// <summary>
    /// Runs all <paramref name="requests"/> through a
    /// <c>GroupByRequestEndpoint → MergeSubstreams</c> pipeline and returns the
    /// fully ordered output list.  Items carry their affinity-slot ID in
    /// <see cref="GroupByRequestEndpointStage{T}.ConnectionAffinitySlot"/> after
    /// the stage stamps them; callers use <see cref="SlotOf"/> to read it.
    /// </summary>
    private async Task<IReadOnlyList<HttpRequestMessage>> RunWithMergeAsync(
        IEnumerable<HttpRequestMessage> requests,
        uint maxSubstreams = 32,
        int maxSlotsPerKey = 6)
    {
        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestEndpoint(
                    RequestEndpoint.FromRequest,
                    maxSubstreams: maxSubstreams,
                    maxSubstreamsPerKey: _ => maxSlotsPerKey)
                .MergeSubstreams();

        return await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);
    }

    // GBEF-001 — 6 requests to the same endpoint spread across up to 6 slots

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_use_up_to_6_slots_when_6_requests_sent_to_same_endpoint()
    {
        // Arrange — 6 requests all targeting the same host:port.
        var requests = Enumerable.Range(1, 6)
            .Select(i => Req($"http://api.example.com/item/{i}"))
            .ToList();

        // Act — run through the full pipeline so each slot's internal queue is
        // drained and the items are delivered to the sink.
        var results = await RunWithMergeAsync(requests, maxSlotsPerKey: 6);

        // Assert — all 6 requests delivered.
        Assert.Equal(6, results.Count);

        // Each item carries the affinity slot ID stamped by the stage.
        // With maxSubstreamsPerKey = 6 there should be between 1 and 6 distinct slots.
        var distinctSlots = results.Select(SlotOf).Distinct().Count();
        Assert.InRange(distinctSlots, 1, 6);
    }

    // GBEF-002 — maxSubstreamsPerKey = 1 forces all requests through one slot

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_use_exactly_one_slot_when_max_substreams_per_key_is_one()
    {
        // Arrange — 6 requests to the same host, but only 1 slot allowed per key.
        var requests = Enumerable.Range(1, 6)
            .Select(i => Req($"http://single-slot.example.com/{i}"))
            .ToList();

        // Act
        var results = await RunWithMergeAsync(requests, maxSlotsPerKey: 1);

        // Assert — all 6 delivered, all with the same slot ID.
        Assert.Equal(6, results.Count);

        var distinctSlots = results.Select(SlotOf).Distinct().ToList();
        Assert.Single(distinctSlots);
    }

    // GBEF-003 — Different endpoints each get their own independent slot group

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_use_independent_slot_groups_when_requests_target_different_endpoints()
    {
        // Arrange — 2 requests per host, 3 different hosts.
        var requests = new List<HttpRequestMessage>
        {
            Req("http://host-a.example.com/1"),
            Req("http://host-b.example.com/1"),
            Req("http://host-c.example.com/1"),
            Req("http://host-a.example.com/2"),
            Req("http://host-b.example.com/2"),
            Req("http://host-c.example.com/2"),
        };

        // Act
        var results = await RunWithMergeAsync(requests, maxSlotsPerKey: 6);

        // Assert — all 6 delivered.
        Assert.Equal(6, results.Count);

        // Slot IDs are global (Interlocked counter) so requests to different
        // hosts will always get distinct IDs — expect at least 3.
        var distinctSlots = results.Select(SlotOf).Distinct().Count();
        Assert.True(distinctSlots >= 3,
            $"Expected at least 3 distinct slot IDs (one per host), but got {distinctSlots}");
    }

    // GBEF-004 — Affinity tagging: request pre-tagged with ConnectionAffinitySlot
    //            is routed to the existing slot without creating a new substream

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_route_to_same_slot_when_request_has_affinity_tag()
    {
        // Arrange — send the initial request so a slot is created and tagged.
        // Then send a second request that already carries the same affinity slot ID
        // (simulating a redirect/retry re-injection).

        const string url = "http://affinity.example.com/resource";

        // Use a queue source so we can inject items one at a time, giving the stage
        // time to stamp the affinity tag on the first item before we construct
        // the re-injected second item.
        var mergeFlow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestEndpoint(
                    RequestEndpoint.FromRequest,
                    maxSubstreams: 32,
                    maxSubstreamsPerKey: _ => 6)
                .MergeSubstreams();

        var (queue, resultsTask) = Source
            .Queue<HttpRequestMessage>(8, OverflowStrategy.Backpressure)
            .Via(mergeFlow)
            .ToMaterialized(Sink.Seq<HttpRequestMessage>(), Keep.Both)
            .Run(Materializer);

        // Offer the first request; stage creates a slot and stamps the affinity tag.
        var firstRequest = Req(url);
        await queue.OfferAsync(firstRequest)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Give the stage time to process the push and stamp the affinity tag.
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        // Read back the slot ID stamped by the stage.
        var hasTag = firstRequest.Options.TryGetValue(
            GroupByRequestEndpointStage<HttpRequestMessage>.ConnectionAffinitySlot,
            out var originalSlotId);

        Assert.True(hasTag, "Stage must stamp the first request with a ConnectionAffinitySlot tag");

        // Offer a re-injected request that already carries the same slot tag
        // (simulating a redirect/retry re-injection from downstream).
        var reinjected = Req(url);
        reinjected.Options.Set(
            GroupByRequestEndpointStage<HttpRequestMessage>.ConnectionAffinitySlot,
            originalSlotId);

        await queue.OfferAsync(reinjected)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Complete the queue so the stage can drain and shut down.
        queue.Complete();

        var results = await resultsTask
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Both requests must be delivered and both must carry the same slot ID,
        // proving the re-injected request was routed to the original slot.
        Assert.Equal(2, results.Count);

        var slots = results.Select(SlotOf).Distinct().ToList();
        Assert.Single(slots);
        Assert.Equal(originalSlotId, slots[0]);
    }

    // GBEF-005 — All 6 requests pass through the pipeline end-to-end

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_deliver_all_requests_when_max_substreams_per_key_is_6()
    {
        // Arrange — 6 requests to the same endpoint.
        var requests = Enumerable.Range(1, 6)
            .Select(i => Req($"http://throughput.example.com/item/{i}"))
            .ToList();

        // Act
        var results = await RunWithMergeAsync(requests, maxSlotsPerKey: 6);

        // Assert — no requests dropped; all 6 must exit the pipeline.
        Assert.Equal(6, results.Count);
    }

    // GBEF-006 — Slot cap: with maxSlotsPerKey = 6 and 10 requests only ≤ 6
    //            distinct slots are ever created (excess items use existing slots)

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_cap_slot_count_when_more_requests_than_max_substreams_per_key()
    {
        // Arrange — 10 requests to the same endpoint; only 6 slots allowed.
        var requests = Enumerable.Range(1, 10)
            .Select(i => Req($"http://capped.example.com/item/{i}"))
            .ToList();

        // Act
        var results = await RunWithMergeAsync(requests, maxSlotsPerKey: 6);

        // Assert — all 10 delivered and at most 6 distinct slots used.
        Assert.Equal(10, results.Count);

        var distinctSlots = results.Select(SlotOf).Distinct().Count();
        Assert.True(distinctSlots <= 6,
            $"Expected at most 6 distinct slot IDs, but got {distinctSlots}");
    }

    // GBEF-007 — Least-loaded routing: all 10 requests delivered even after
    //            the per-key slot cap is reached

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_deliver_all_requests_when_slot_cap_reached_and_least_loaded_routing_applies()
    {
        // Arrange — 10 requests to the same host, slot cap = 6.
        // The 7th–10th requests route to the least-loaded existing slot.
        var requests = Enumerable.Range(1, 10)
            .Select(i => Req($"http://least-loaded.example.com/item/{i}"))
            .ToList();

        // Act
        var results = await RunWithMergeAsync(requests, maxSlotsPerKey: 6);

        // Assert — no requests lost; all 10 must arrive.
        Assert.Equal(10, results.Count);
    }

    // GBEF-008 — Global maxSubstreams cap is respected across multiple endpoints

    [Fact(Timeout = 10_000)]
    public async Task GroupByEndpointFanOut_should_respect_global_max_substreams_when_multiple_endpoints_compete()
    {
        // Arrange — 4 hosts × 3 requests each = 12 requests, interleaved by host.
        // With maxSubstreams = 4 the stage can create at most 4 slots total;
        // subsequent requests for a new host are queued into existing slots once
        // the global cap is hit.
        var requests = new List<HttpRequestMessage>();
        for (var i = 0; i < 3; i++)
        {
            for (var host = 0; host < 4; host++)
            {
                requests.Add(Req($"http://ep{host}.example.com/item/{i}"));
            }
        }

        // Act
        var results = await RunWithMergeAsync(requests, maxSubstreams: 4, maxSlotsPerKey: 6);

        // Assert — all 12 requests delivered and no more than 4 distinct slots used.
        Assert.Equal(12, results.Count);

        var distinctSlots = results.Select(SlotOf).Distinct().Count();
        Assert.True(distinctSlots <= 4,
            $"Expected at most 4 distinct slot IDs across all endpoints, but got {distinctSlots}");
    }
}
