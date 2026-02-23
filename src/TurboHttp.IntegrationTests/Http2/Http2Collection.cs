using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// xUnit collection that shares a single <see cref="KestrelH2Fixture"/> across all HTTP/2
/// integration test classes. Tests in the collection run sequentially to avoid port conflicts.
/// </summary>
[CollectionDefinition("Http2Integration")]
public sealed class Http2Collection : ICollectionFixture<KestrelH2Fixture>;
