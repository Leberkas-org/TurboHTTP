namespace TurboHTTP.IntegrationTests.Shared;

/// <summary>
/// Stub interface to support test fixtures without requiring xunit.v3.mtp-v2 in a non-executable project.
/// The actual IAsyncLifetime comes from xunit in the executable test project.
/// </summary>
public interface IAsyncLifetime
{
    ValueTask InitializeAsync();
    ValueTask DisposeAsync();
}
