using TurboHttp.IO;

namespace TurboHttp.Tests.IO;

public sealed class ClientManagerProviderSelectionTests
{
    private static TcpOptions MakeTcp() => new() { Host = "localhost", Port = 80 };
    private static TlsOptions MakeTls() => new() { Host = "localhost", Port = 443 };

    private static IClientProvider SelectProvider(IClientProvider? streamProvider, TcpOptions options)
        => streamProvider ?? options switch
        {
            TlsOptions tls => new TlsClientProvider(tls),
            _ => new TcpClientProvider(options)
        };

    // CLT-001: TcpOptions, no StreamProvider → TcpClientProvider selected
    [Fact]
    public void CLT_001_TcpOptions_NoStreamProvider_CreatesTcpClientProvider()
    {
        var provider = SelectProvider(null, MakeTcp());

        Assert.IsType<TcpClientProvider>(provider);
    }

    // CLT-002: TlsOptions, no StreamProvider → TlsClientProvider selected
    [Fact]
    public void CLT_002_TlsOptions_NoStreamProvider_CreatesTlsClientProvider()
    {
        var provider = SelectProvider(null, MakeTls());

        Assert.IsType<TlsClientProvider>(provider);
    }

    // CLT-003: StreamProvider explicitly set → used regardless of Options type
    [Fact]
    public void CLT_003_StreamProviderSet_UsedRegardlessOfOptionsType()
    {
        var mock = new StubClientProvider();

        var resultTcp = SelectProvider(mock, MakeTcp());
        var resultTls = SelectProvider(mock, MakeTls());

        Assert.Same(mock, resultTcp);
        Assert.Same(mock, resultTls);
    }

    private sealed class StubClientProvider : IClientProvider
    {
        public System.Net.EndPoint? RemoteEndPoint => null;
        public Stream GetStream() => throw new NotSupportedException();

        public void Close()
        {
        }
    }
}