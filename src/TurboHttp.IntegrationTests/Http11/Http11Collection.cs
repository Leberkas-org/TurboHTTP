using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// xUnit collection that shares a single <see cref="KestrelFixture"/> across all HTTP/1.1
/// integration test classes. Tests in the collection run sequentially to avoid port conflicts.
/// </summary>
[CollectionDefinition("Http11Integration")]
public sealed class Http11Collection : ICollectionFixture<KestrelFixture>;
