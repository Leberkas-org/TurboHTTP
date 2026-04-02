namespace TurboHttp.Internal;

/// <summary>
/// DI marker registered once per <c>AddTurboHttpClient()</c> call.
/// Resolved via <c>IServiceProvider.GetServices&lt;TurboHttpClientName&gt;()</c>
/// to determine the maximum <see cref="TurboClientOptions.MaxEndpointSubstreams"/>
/// across all registered clients for dispatcher thread sizing.
/// </summary>
internal sealed record TurboHttpClientName(string Name);
