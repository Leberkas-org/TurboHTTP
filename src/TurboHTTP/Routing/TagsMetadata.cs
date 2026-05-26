namespace TurboHTTP.Routing;

public sealed record TagsMetadata(IReadOnlyList<string> Tags) : ITagsMetadata;
