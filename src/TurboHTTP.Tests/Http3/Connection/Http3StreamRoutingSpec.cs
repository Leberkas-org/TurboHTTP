using Servus.Akka.IO;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3StreamRoutingSpec
{
    private readonly FakeOps _ops = new();
    private readonly QpackTableSync _tableSync = new();

    private StateMachine CreateMachine(FakeOps? ops = null)
    {
        return new StateMachine(
            new TurboClientOptions(),
            ops ?? _ops);
    }

    private Http3HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new Http3HeadersFrame(_tableSync.Encoder.Encode(headers));
    }

    private NetworkBuffer BuildResponseBuffer(byte fillByte, int bodySize)
    {
        var headersFrame = EncodeHeaders((":status", "200"));
        var body = new byte[bodySize];
        Array.Fill(body, fillByte);
        var dataFrame = new Http3DataFrame(body);

        var totalSize = headersFrame.SerializedSize + dataFrame.SerializedSize;
        var buf = NetworkBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        headersFrame.WriteTo(ref span);
        dataFrame.WriteTo(ref span);
        buf.Length = totalSize;
        return buf;
    }

    private static NetworkBuffer BuildDataBuffer(byte fillByte, int bodySize)
    {
        var body = new byte[bodySize];
        Array.Fill(body, fillByte);
        var dataFrame = new Http3DataFrame(body);

        var buf = NetworkBuffer.Rent(dataFrame.SerializedSize);
        var span = buf.FullMemory.Span;
        dataFrame.WriteTo(ref span);
        buf.Length = dataFrame.SerializedSize;
        return buf;
    }

    [Fact(Timeout = 5000)]
    public void DecodeServerData_should_use_per_stream_decoders()
    {
        var sm = CreateMachine();

        // Stream 0: HEADERS + 1KB DATA
        var buf0 = BuildResponseBuffer(0xAA, 1024);
        var frames0 = sm.DecodeServerData(buf0, streamId: 0);

        Assert.Equal(2, frames0.Count);
        Assert.IsType<Http3HeadersFrame>(frames0[0]);
        Assert.IsType<Http3DataFrame>(frames0[1]);

        // Stream 4: HEADERS + 1KB DATA
        var buf4 = BuildResponseBuffer(0xBB, 1024);
        var frames4 = sm.DecodeServerData(buf4, streamId: 4);

        Assert.Equal(2, frames4.Count);
        Assert.IsType<Http3HeadersFrame>(frames4[0]);
        Assert.IsType<Http3DataFrame>(frames4[1]);

        // Verify data integrity — stream 4's DATA should be 0xBB, not contaminated by stream 0
        var data4 = ((Http3DataFrame)frames4[1]).Data.Span;
        Assert.Equal(1024, data4.Length);
        Assert.True(data4.ToArray().All(b => b == 0xBB));

        sm.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AssembleResponse_should_route_data_to_correct_stream_with_60KB_bodies()
    {
        var sm = CreateMachine();
        const int bodySize = 60 * 1024; // 60KB per stream

        // Simulate two concurrent request streams
        // Stream 0: filled with 0xAA
        // Stream 4: filled with 0xBB

        // Decode HEADERS + partial DATA for stream 0
        var buf0 = BuildResponseBuffer(0xAA, bodySize / 2);
        var frames0 = sm.DecodeServerData(buf0, streamId: 0);
        foreach (var f in frames0)
        {
            var forwarded = sm.ProcessFrame(f);
            if (forwarded is not null)
            {
                sm.AssembleResponse(forwarded, streamId: 0);
            }
        }

        // Interleave: decode HEADERS + partial DATA for stream 4
        var buf4 = BuildResponseBuffer(0xBB, bodySize / 2);
        var frames4 = sm.DecodeServerData(buf4, streamId: 4);
        foreach (var f in frames4)
        {
            var forwarded = sm.ProcessFrame(f);
            if (forwarded is not null)
            {
                sm.AssembleResponse(forwarded, streamId: 4);
            }
        }

        // More DATA for stream 0 (second half)
        var buf0b = BuildDataBuffer(0xAA, bodySize / 2);
        var frames0b = sm.DecodeServerData(buf0b, streamId: 0);
        foreach (var f in frames0b)
        {
            var forwarded = sm.ProcessFrame(f);
            if (forwarded is not null)
            {
                sm.AssembleResponse(forwarded, streamId: 0);
            }
        }

        // More DATA for stream 4 (second half)
        var buf4b = BuildDataBuffer(0xBB, bodySize / 2);
        var frames4b = sm.DecodeServerData(buf4b, streamId: 4);
        foreach (var f in frames4b)
        {
            var forwarded = sm.ProcessFrame(f);
            if (forwarded is not null)
            {
                sm.AssembleResponse(forwarded, streamId: 4);
            }
        }

        // Flush both responses
        sm.FlushPendingResponse(0);
        sm.FlushPendingResponse(4);

        Assert.Equal(2, _ops.Responses.Count);

        // Verify stream 0 response body is all 0xAA
        var body0 = await _ops.Responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodySize, body0.Length);
        Assert.True(body0.All(b => b == 0xAA), "Stream 0 body corrupted — contains bytes from another stream");

        // Verify stream 4 response body is all 0xBB
        var body4 = await _ops.Responses[1].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodySize, body4.Length);
        Assert.True(body4.All(b => b == 0xBB), "Stream 4 body corrupted — contains bytes from another stream");

        sm.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void DecodeServerData_should_handle_partial_frames_across_buffers_per_stream()
    {
        var sm = CreateMachine();

        // Build a DATA frame and split it mid-frame to test remainder handling
        var body = new byte[256];
        Array.Fill(body, (byte)0xCC);
        var dataFrame = new Http3DataFrame(body);
        var serialized = dataFrame.Serialize();

        // Split at byte 10 (mid-frame)
        var part1 = NetworkBuffer.Rent(10);
        serialized.AsSpan(0, 10).CopyTo(part1.FullMemory.Span);
        part1.Length = 10;

        var part2 = NetworkBuffer.Rent(serialized.Length - 10);
        serialized.AsSpan(10).CopyTo(part2.FullMemory.Span);
        part2.Length = serialized.Length - 10;

        // Feed part 1 to stream 0 — should buffer remainder, no complete frame
        var frames1 = sm.DecodeServerData(part1, streamId: 0);
        Assert.Empty(frames1);

        // Feed unrelated data to stream 4 — should NOT interfere with stream 0's remainder
        var headersFrame = EncodeHeaders((":status", "200"));
        var hdrBuf = NetworkBuffer.Rent(headersFrame.SerializedSize);
        var hdrSpan = hdrBuf.FullMemory.Span;
        headersFrame.WriteTo(ref hdrSpan);
        hdrBuf.Length = headersFrame.SerializedSize;

        var stream4Frames = sm.DecodeServerData(hdrBuf, streamId: 4);
        Assert.Single(stream4Frames);
        Assert.IsType<Http3HeadersFrame>(stream4Frames[0]);

        // Feed part 2 to stream 0 — should complete the DATA frame
        var frames2 = sm.DecodeServerData(part2, streamId: 0);
        Assert.Single(frames2);
        var decoded = Assert.IsType<Http3DataFrame>(frames2[0]);
        Assert.Equal(256, decoded.Data.Length);
        Assert.True(decoded.Data.Span.ToArray().All(b => b == 0xCC));

        sm.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void DecodeServerData_should_isolate_control_stream_from_request_streams()
    {
        var sm = CreateMachine();
        const long controlStreamId = -2; // Matches ControlStreamDecoderId in Http30ConnectionStage

        // Feed SETTINGS on control stream
        var settings = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 8192)]);
        var settingsBuf = NetworkBuffer.Rent(settings.SerializedSize);
        var settingsSpan = settingsBuf.FullMemory.Span;
        settings.WriteTo(ref settingsSpan);
        settingsBuf.Length = settings.SerializedSize;

        var controlFrames = sm.DecodeServerData(settingsBuf, streamId: controlStreamId);
        Assert.Single(controlFrames);
        Assert.IsType<Http3SettingsFrame>(controlFrames[0]);

        // Feed HEADERS + DATA on request stream 0 — should not be affected by control stream
        var reqBuf = BuildResponseBuffer(0xDD, 512);
        var reqFrames = sm.DecodeServerData(reqBuf, streamId: 0);
        Assert.Equal(2, reqFrames.Count);
        Assert.IsType<Http3HeadersFrame>(reqFrames[0]);
        var reqData = Assert.IsType<Http3DataFrame>(reqFrames[1]);
        Assert.Equal(512, reqData.Data.Length);
        Assert.True(reqData.Data.Span.ToArray().All(b => b == 0xDD));

        sm.Dispose();
    }
}