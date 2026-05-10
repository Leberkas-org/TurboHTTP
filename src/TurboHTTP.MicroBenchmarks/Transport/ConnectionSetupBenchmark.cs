using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;

namespace TurboHTTP.MicroBenchmarks.Transport;

[Config(typeof(MicroBenchmarkConfig))]
public class ConnectionSetupBenchmark
{
    private TcpListener _listener = null!;
    private int _port;
    private Task _acceptLoop = null!;
    private CancellationTokenSource _cts = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _acceptLoop = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    client.Close();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cts.Cancel();
        _listener.Stop();
        _acceptLoop.Wait(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task TcpLoopbackConnect()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
    }
}
