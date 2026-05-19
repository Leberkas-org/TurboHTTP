using TurboHTTP.Server;

namespace TurboHTTP.Routing;

internal sealed class EntityResponseMapperCollection
{
    private readonly List<(Type Type, Func<TurboHttpContext, object, Task> Mapper)> _mappers = [];

    public void Add<T>(Func<TurboHttpContext, T, Task> mapper)
    {
        _mappers.Add((typeof(T), (ctx, obj) => mapper(ctx, (T)obj)));
    }

    public Func<TurboHttpContext, object, Task>? FindMapper(Type responseType)
    {
        foreach (var (type, mapper) in _mappers)
        {
            if (type == responseType)
            {
                return mapper;
            }
        }

        foreach (var (type, mapper) in _mappers)
        {
            if (type.IsAssignableFrom(responseType))
            {
                return mapper;
            }
        }

        return null;
    }
}
