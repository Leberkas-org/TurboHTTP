using BenchmarkDotNet.Attributes;

namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Base class for all Binkraken remote HTTPS benchmarks. Provides static URIs
/// for the light (~3 KB HTML) and heavy (~129 KB JS bundle) endpoints.
/// </summary>
public abstract class BinkrakenBaseClass : BenchmarkSuiteBase
{
    [Params("1.1", "2.0")]
    public new string HttpVersion { get; set; } = "1.1";
    /// <summary>
    /// Light endpoint: the SPA index page (~3 KB HTML).
    /// </summary>
    public static readonly Uri LightUri = new("https://binkraken.com/");

    /// <summary>
    /// Heavy endpoint: the largest JS bundle (~129 KB).
    /// </summary>
    public static readonly Uri HeavyUri = new("https://binkraken.com/assets/useBlog-CU_ZN4Zc.js");
}
