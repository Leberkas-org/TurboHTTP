using Servus.Akka.IO;
using Servus.Akka.IO.Quic;
using Servus.Akka.Tests.Utils;

namespace Servus.Akka.Tests.IO.Quic;

public sealed class TypedStreamStateSpec
{
    [Fact(Timeout = 5000)]
    public void TypedStreamState_should_have_null_handle_by_default()
    {
        var state = new TypedStreamState();

        Assert.Null(state.Handle);
    }

    [Fact(Timeout = 5000)]
    public void TypedStreamState_should_have_empty_pending_items_by_default()
    {
        var state = new TypedStreamState();

        Assert.Empty(state.PendingItems);
    }

    [Fact(Timeout = 5000)]
    public void TypedStreamState_should_have_zero_stream_id_by_default()
    {
        var state = new TypedStreamState();

        Assert.Equal(0, state.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void TypedStreamState_should_have_zero_original_synthetic_stream_id_by_default()
    {
        var state = new TypedStreamState();

        Assert.Equal(0, state.OriginalSyntheticStreamId);
    }

    [Fact(Timeout = 5000)]
    public void TypedStreamState_should_have_false_is_outbound_by_default()
    {
        var state = new TypedStreamState();

        Assert.False(state.IsOutbound);
    }

    [Fact(Timeout = 5000)]
    public void TypedStreamState_should_allow_setting_all_fields()
    {
        var state = new TypedStreamState
        {
            StreamId = 42,
            OriginalSyntheticStreamId = -2,
            IsOutbound = true
        };

        Assert.Equal(42, state.StreamId);
        Assert.Equal(-2, state.OriginalSyntheticStreamId);
        Assert.True(state.IsOutbound);
    }

    [Fact(Timeout = 5000)]
    public void TypedStreamState_PendingItems_should_support_enqueue_dequeue()
    {
        var state = new TypedStreamState();

        var buf1 = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var buf2 = NetworkBufferTestExtensions.FromArray([4, 5, 6]);

        state.PendingItems.Enqueue(buf1);
        state.PendingItems.Enqueue(buf2);

        Assert.Equal(2, state.PendingItems.Count);

        var first = state.PendingItems.Dequeue();
        Assert.Same(buf1, first);

        first.Dispose();
        state.PendingItems.Dequeue().Dispose();
    }
}
