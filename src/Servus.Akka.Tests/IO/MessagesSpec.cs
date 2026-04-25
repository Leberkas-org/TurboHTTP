using System.Net;
using Servus.Akka.IO;
using Servus.Akka.IO.Tcp;
using Servus.Akka.Tests.Utils;

namespace Servus.Akka.Tests.IO;

public sealed class MessagesSpec
{
    private static readonly RequestEndpoint TestKey = new()
    {
        Scheme = "https",
        Host = "localhost",
        Port = 443,
        Version = HttpVersion.Version20
    };

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_Rent_should_return_buffer_with_capacity()
    {
        var buf = NetworkBuffer.Rent(1024);

        Assert.True(buf.Capacity >= 1024);
        Assert.Equal(0, buf.Length);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_Rent_should_have_key()
    {
        var buf = NetworkBuffer.Rent(64);

        Assert.Equal(string.Empty, buf.Key.Host);
        Assert.Equal(string.Empty, buf.Key.Scheme);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_should_expose_memory_up_to_length()
    {
        var buf = NetworkBuffer.Rent(256);
        buf.Length = 10;

        Assert.Equal(10, buf.Memory.Length);
        Assert.Equal(10, buf.Span.Length);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_should_expose_full_memory()
    {
        var buf = NetworkBuffer.Rent(256);
        buf.Length = 10;

        Assert.True(buf.FullMemory.Length >= 256);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_Dispose_should_be_idempotent()
    {
        var buf = NetworkBuffer.Rent(64);

        buf.Dispose();
        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_Capacity_should_be_zero_after_dispose()
    {
        var buf = NetworkBuffer.Rent(64);

        buf.Dispose();

        Assert.Equal(0, buf.Capacity);
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_Key_should_be_settable()
    {
        var buf = NetworkBuffer.Rent(64);
        buf.Key = TestKey;

        Assert.Equal(TestKey, buf.Key);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_Length_should_be_settable()
    {
        var buf = NetworkBuffer.Rent(256);
        buf.Length = 128;

        Assert.Equal(128, buf.Length);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void RoutedNetworkBuffer_Rent_should_return_buffer_with_null_stream_fields()
    {
        var buf = RoutedNetworkBuffer.Rent(1024);

        Assert.Null(buf.StreamTypeValue);
        Assert.Null(buf.StreamId);
        Assert.True(buf.Capacity >= 1024);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void RoutedNetworkBuffer_should_allow_setting_stream_fields()
    {
        var buf = RoutedNetworkBuffer.Rent(64);
        buf.StreamTypeValue = 0x00;
        buf.StreamId = 42;

        Assert.Equal(0x00, buf.StreamTypeValue);
        Assert.Equal(42, buf.StreamId);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void RoutedNetworkBuffer_Dispose_should_be_idempotent()
    {
        var buf = RoutedNetworkBuffer.Rent(64);

        buf.Dispose();
        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void RoutedNetworkBuffer_Capacity_should_be_zero_after_dispose()
    {
        var buf = RoutedNetworkBuffer.Rent(64);

        buf.Dispose();

        Assert.Equal(0, buf.Capacity);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionReuseItem_should_preserve_fields()
    {
        var item = new ConnectionReuseItem(true) { Key = TestKey };

        Assert.True(item.CanReuse);
        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionReuseItem_equality_should_compare_all_fields()
    {
        var a = new ConnectionReuseItem(true) { Key = TestKey };
        var b = new ConnectionReuseItem(true) { Key = TestKey };
        var c = new ConnectionReuseItem(false) { Key = TestKey };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact(Timeout = 5000)]
    public void ConnectItem_should_preserve_fields()
    {
        var opts = new TcpOptions { Host = "localhost", Port = 443 };
        var item = new ConnectItem(opts) { Key = TestKey, IsReconnect = true };

        Assert.Same(opts, item.Options);
        Assert.Equal(TestKey, item.Key);
        Assert.True(item.IsReconnect);
    }

    [Fact(Timeout = 5000)]
    public void ConnectItem_IsReconnect_should_default_to_false()
    {
        var opts = new TcpOptions { Host = "localhost", Port = 443 };
        var item = new ConnectItem(opts) { Key = TestKey };

        Assert.False(item.IsReconnect);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentStreamsItem_should_preserve_fields()
    {
        var item = new MaxConcurrentStreamsItem(42) { Key = TestKey };

        Assert.Equal(42, item.MaxStreams);
        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void StreamAcquireItem_should_preserve_key()
    {
        var item = new StreamAcquireItem { Key = TestKey };

        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void CloseSignalItem_should_preserve_fields()
    {
        var item = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = TestKey };

        Assert.Equal(TlsCloseKind.AbruptClose, item.CloseKind);
        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void ConnectedSignalItem_should_preserve_key()
    {
        var item = new ConnectedSignalItem { Key = TestKey };

        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void TlsCloseKind_should_have_expected_values()
    {
        Assert.Equal(0, (int)TlsCloseKind.CleanClose);
        Assert.Equal(1, (int)TlsCloseKind.AbruptClose);
    }

    [Fact(Timeout = 5000)]
    public void QuicCloseKind_should_have_expected_values()
    {
        Assert.Equal(0, (int)QuicCloseKind.RequestStreamComplete);
        Assert.Equal(1, (int)QuicCloseKind.ConnectionFailure);
        Assert.Equal(2, (int)QuicCloseKind.MigrationDisallowed);
        Assert.Equal(3, (int)QuicCloseKind.WriteFailed);
        Assert.Equal(4, (int)QuicCloseKind.AcquisitionFailed);
    }

    [Fact(Timeout = 5000)]
    public void QuicCloseItem_should_preserve_fields()
    {
        var item = new QuicCloseItem(QuicCloseKind.ConnectionFailure, 7) { Key = TestKey };

        Assert.Equal(QuicCloseKind.ConnectionFailure, item.Kind);
        Assert.Equal(7, item.StreamId);
        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void QuicCloseItem_StreamId_should_default_to_minus_one()
    {
        var item = new QuicCloseItem(QuicCloseKind.RequestStreamComplete);

        Assert.Equal(-1, item.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void OpenTypedStreamItem_should_preserve_fields()
    {
        var item = new OpenTypedStreamItem(0x00, -2, true) { Key = TestKey };

        Assert.Equal(0x00, item.StreamTypeValue);
        Assert.Equal(-2, item.SyntheticStreamId);
        Assert.True(item.Outbound);
        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void Http3EndOfRequestItem_should_preserve_fields()
    {
        var item = new Http3EndOfRequestItem { Key = TestKey, StreamId = 99 };

        Assert.Equal(TestKey, item.Key);
        Assert.Equal(99, item.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void ProtocolReadyItem_should_preserve_key()
    {
        var item = new ProtocolReadyItem { Key = TestKey };

        Assert.Equal(TestKey, item.Key);
    }

    [Fact(Timeout = 5000)]
    public void NetworkBuffer_ConfigurePoolSize_should_update_pool()
    {
        var original = Environment.ProcessorCount * 2;
        try
        {
            NetworkBuffer.ConfigurePoolSize(4);

            var buf = NetworkBuffer.Rent(64);
            buf.Dispose();
        }
        finally
        {
            NetworkBuffer.ConfigurePoolSize(original);
        }
    }
}
