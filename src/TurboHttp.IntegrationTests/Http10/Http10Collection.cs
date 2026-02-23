using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// xUnit collection that shares a single <see cref="KestrelFixture"/> across all HTTP/1.0
/// integration test classes. Tests in the collection run sequentially to avoid port conflicts.
/// </summary>
[CollectionDefinition("Http10Integration")]
public sealed class Http10Collection : ICollectionFixture<KestrelFixture>;
