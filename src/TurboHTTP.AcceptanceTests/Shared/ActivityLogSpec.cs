using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Shared;

public sealed class ActivityLogSpec
{
    [Fact(Timeout = 5000)]
    public void ActivityLog_should_start_empty()
    {
        var log = new ActivityLog();
        Assert.Empty(log.Entries);
    }

    [Fact(Timeout = 5000)]
    public void ActivityLog_should_record_activity_in_order()
    {
        var log = new ActivityLog();
        var a = new WriteAttempt(0, [1, 2, 3]);
        var b = new ResponseDelivered(0, 42);
        log.Record(a);
        log.Record(b);
        Assert.Equal(2, log.Entries.Count);
        Assert.Same(a, log.Entries[0]);
        Assert.Same(b, log.Entries[1]);
    }

    [Fact(Timeout = 5000)]
    public void ActivityLog_should_filter_by_subtype_via_OfType()
    {
        var log = new ActivityLog();
        log.Record(new WriteAttempt(0, [1]));
        log.Record(new DisconnectEvent("timeout"));
        log.Record(new WriteAttempt(1, [2]));
        log.Record(new ConnectionAbort());

        var writes = log.OfType<WriteAttempt>().ToList();
        Assert.Equal(2, writes.Count);
        Assert.Equal(0, writes[0].Index);
        Assert.Equal(1, writes[1].Index);
    }

    [Fact(Timeout = 5000)]
    public void ActivityLog_should_return_empty_sequence_when_no_matching_subtype()
    {
        var log = new ActivityLog();
        log.Record(new WriteAttempt(0, []));
        Assert.Empty(log.OfType<ResponseDelivered>());
    }

    [Fact(Timeout = 5000)]
    public void ActivityLog_should_clear_all_entries()
    {
        var log = new ActivityLog();
        log.Record(new WriteAttempt(0, [1]));
        log.Record(new ConnectionAbort());
        log.Clear();
        Assert.Empty(log.Entries);
    }

    [Fact(Timeout = 5000)]
    public void ActivityLog_should_record_after_clear()
    {
        var log = new ActivityLog();
        log.Record(new WriteAttempt(0, [1]));
        log.Clear();
        log.Record(new ResponseDelivered(0, 100));
        Assert.Single(log.Entries);
        Assert.IsType<ResponseDelivered>(log.Entries[0]);
    }

    [Fact(Timeout = 5000)]
    public void WriteAttempt_should_carry_index_and_payload()
    {
        var payload = new byte[] { 10, 20, 30 };
        var entry = new WriteAttempt(3, payload);
        Assert.Equal(3, entry.Index);
        Assert.Same(payload, entry.Payload);
    }

    [Fact(Timeout = 5000)]
    public void DisconnectEvent_should_carry_reason()
    {
        var entry = new DisconnectEvent("peer reset");
        Assert.Equal("peer reset", entry.Reason);
    }

    [Fact(Timeout = 5000)]
    public void ResponseDelivered_should_carry_index_and_byte_count()
    {
        var entry = new ResponseDelivered(5, 1024);
        Assert.Equal(5, entry.Index);
        Assert.Equal(1024, entry.ByteCount);
    }

    [Fact(Timeout = 5000)]
    public void Activity_records_should_have_timestamp_set_on_construction()
    {
        var before = DateTimeOffset.UtcNow;
        var entry = new ConnectionAbort();
        var after = DateTimeOffset.UtcNow;
        Assert.InRange(entry.Timestamp, before, after);
    }

    [Fact(Timeout = 5000)]
    public void ActivityLog_should_preserve_chronological_order_across_mixed_types()
    {
        var log = new ActivityLog();
        log.Record(new WriteAttempt(0, []));
        log.Record(new ResponseDelivered(0, 50));
        log.Record(new DisconnectEvent("done"));
        log.Record(new ConnectionAbort());

        Assert.IsType<WriteAttempt>(log.Entries[0]);
        Assert.IsType<ResponseDelivered>(log.Entries[1]);
        Assert.IsType<DisconnectEvent>(log.Entries[2]);
        Assert.IsType<ConnectionAbort>(log.Entries[3]);
    }
}
