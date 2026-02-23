using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// xUnit collection that shares a single <see cref="KestrelH2Fixture"/> across all
/// Phase 16 HTTP/2 Advanced integration test classes. Tests in the collection run
/// sequentially to avoid port conflicts.
/// </summary>
[CollectionDefinition("Http2Advanced")]
public sealed class Http2AdvancedCollection : ICollectionFixture<KestrelH2Fixture>;
