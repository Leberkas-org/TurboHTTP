using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboRouteGroupBuilder
{
    private readonly string _prefix;
    private readonly TurboRouteTable _table;
    private readonly List<object> _groupMetadata = [];
    private readonly List<string> _tags = [];

    internal TurboRouteGroupBuilder(string prefix, TurboRouteTable table)
    {
        _prefix = prefix;
        _table = table;
    }

    private TurboRouteGroupBuilder(string prefix, TurboRouteTable table, List<object> parentMetadata, List<string> parentTags)
    {
        _prefix = prefix;
        _table = table;
        _groupMetadata.AddRange(parentMetadata);
        _tags.AddRange(parentTags);
    }

    public TurboRouteHandlerBuilder MapGet(string pattern, Delegate handler)
    {
        var builder = _table.Add("GET", _prefix + pattern, handler);
        ApplyGroupMetadata(builder);
        return builder;
    }

    public TurboRouteHandlerBuilder MapPost(string pattern, Delegate handler)
    {
        var builder = _table.Add("POST", _prefix + pattern, handler);
        ApplyGroupMetadata(builder);
        return builder;
    }

    public TurboRouteHandlerBuilder MapPut(string pattern, Delegate handler)
    {
        var builder = _table.Add("PUT", _prefix + pattern, handler);
        ApplyGroupMetadata(builder);
        return builder;
    }

    public TurboRouteHandlerBuilder MapDelete(string pattern, Delegate handler)
    {
        var builder = _table.Add("DELETE", _prefix + pattern, handler);
        ApplyGroupMetadata(builder);
        return builder;
    }

    public TurboRouteHandlerBuilder MapPatch(string pattern, Delegate handler)
    {
        var builder = _table.Add("PATCH", _prefix + pattern, handler);
        ApplyGroupMetadata(builder);
        return builder;
    }

    public TurboRouteHandlerBuilder MapMethods(string pattern, IEnumerable<string> methods, Delegate handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = _table.Add(method, _prefix + pattern, handler);
            ApplyGroupMetadata(last);
        }

        return last!;
    }

    private void ApplyGroupMetadata(TurboRouteHandlerBuilder builder)
    {
        if (_tags.Count > 0)
        {
            builder.WithTags(_tags.ToArray());
        }

        foreach (var item in _groupMetadata)
        {
            builder.WithMetadata(item);
        }
    }

    public TurboRouteGroupBuilder MapGroup(string prefix)
    {
        return new TurboRouteGroupBuilder(_prefix + prefix, _table, _groupMetadata, _tags);
    }

    public TurboRouteGroupBuilder WithTags(params string[] tags)
    {
        _tags.AddRange(tags);
        return this;
    }

    public TurboRouteGroupBuilder WithMetadata(params object[] metadata)
    {
        _groupMetadata.AddRange(metadata);
        return this;
    }

    public TurboRouteGroupBuilder RequireAuthorization()
    {
        _groupMetadata.Add(new AuthorizeData(null, null, null));
        return this;
    }

    public TurboRouteGroupBuilder RequireAuthorization(string? policy)
    {
        _groupMetadata.Add(new AuthorizeData(policy, null, null));
        return this;
    }

    public TurboRouteGroupBuilder AllowAnonymous()
    {
        _groupMetadata.Add(new AllowAnonymousMarker());
        return this;
    }

    public TurboRouteHandlerBuilder MapEntity(string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(_prefix + pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(_table);
        return new TurboRouteHandlerBuilder();
    }

    public TurboRouteHandlerBuilder MapEntity<TActorKey>(string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(_prefix + pattern).UseActorRef(x => x.Get<TActorKey>());
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(_table);
        return new TurboRouteHandlerBuilder();
    }
}