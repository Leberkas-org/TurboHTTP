using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TurboHTTP.Server;

namespace TurboHTTP.Routing.Binding;

internal sealed class ParameterParseException(string message, Exception innerException) : Exception(message, innerException);

internal abstract class ParameterBinder
{
    public abstract ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services);
}

internal sealed class ContextBinder : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services) =>
        ValueTask.FromResult<object?>(ctx);
}

internal sealed class HttpContextBinder : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services) =>
        ValueTask.FromResult<object?>(ctx);
}

internal sealed class CancellationTokenBinder : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services) =>
        ValueTask.FromResult<object?>(ctx.RequestAborted);
}

internal sealed class RequestBinder : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services) =>
        ValueTask.FromResult<object?>(ctx.Request);
}

internal sealed class RouteValueBinder(string name, Type type) : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
    {
        if (!ctx.Request.RouteValues.TryGetValue(name, out var value) || value is null)
        {
            return ValueTask.FromResult(type.IsValueType ? Activator.CreateInstance(type) : null);
        }

        var str = value.ToString()!;
        return ValueTask.FromResult<object?>(ParseValue(str, type));
    }

    internal static object ParseValue(string str, Type type)
    {
        try
        {
            if (type == typeof(string))
            {
                return str;
            }

            if (type == typeof(int))
            {
                return int.Parse(str, CultureInfo.InvariantCulture);
            }

            if (type == typeof(long))
            {
                return long.Parse(str, CultureInfo.InvariantCulture);
            }

            if (type == typeof(float))
            {
                return float.Parse(str, CultureInfo.InvariantCulture);
            }

            if (type == typeof(double))
            {
                return double.Parse(str, CultureInfo.InvariantCulture);
            }

            if (type == typeof(decimal))
            {
                return decimal.Parse(str, CultureInfo.InvariantCulture);
            }

            if (type == typeof(bool))
            {
                return bool.Parse(str);
            }

            if (type == typeof(Guid))
            {
                return Guid.Parse(str);
            }

            if (type == typeof(DateTime))
            {
                return DateTime.Parse(str, CultureInfo.InvariantCulture);
            }

            if (type == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(str, CultureInfo.InvariantCulture);
            }

            if (type == typeof(TimeSpan))
            {
                return TimeSpan.Parse(str, CultureInfo.InvariantCulture);
            }

            return Convert.ChangeType(str, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new ParameterParseException(
                string.Concat("Failed to parse '", str, "' as type '", type.Name, "'."), ex);
        }
    }
}

internal sealed class HeaderBinder : ParameterBinder
{
    private readonly string _name;
    private readonly Type _type;

    public HeaderBinder(string name, Type type)
    {
        _name = name;
        _type = type;
    }

    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
    {
        var value = ctx.Request.Headers[_name].FirstOrDefault();
        if (value is null)
        {
            return ValueTask.FromResult(
                _type.IsValueType ? Activator.CreateInstance(_type) : null);
        }

        return ValueTask.FromResult<object?>(RouteValueBinder.ParseValue(value, _type));
    }
}

internal sealed class FormBinder(string name, Type type) : ParameterBinder
{
    public override async ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
    {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var value = form[name].FirstOrDefault();
        if (value is null)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        return RouteValueBinder.ParseValue(value, type);
    }
}

internal sealed class FormFileBinder : ParameterBinder
{
    private readonly string _name;

    public FormFileBinder(string name)
    {
        _name = name;
    }

    public override async ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
    {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        return form.Files.GetFile(_name);
    }
}

internal sealed class AsParametersBinder : ParameterBinder
{
    private readonly Type _type;
    private readonly ConstructorBinder[] _ctorBinders;

    public AsParametersBinder(Type type, HashSet<string> routeSegments, HashSet<Type>? visited = null)
    {
        _type = type;
        visited ??= [];

        if (!visited.Add(type))
        {
            throw new InvalidOperationException(
                string.Concat("Circular [AsParameters] reference detected for type '", type.Name, "'."));
        }

        var ctor = type.GetConstructors()
                       .OrderByDescending(c => c.GetParameters().Length)
                       .FirstOrDefault()
                   ?? throw new InvalidOperationException(
                       string.Concat("[AsParameters] type '", type.Name, "' has no accessible constructor."));

        var ctorParams = ctor.GetParameters();
        _ctorBinders = new ConstructorBinder[ctorParams.Length];

        for (var i = 0; i < ctorParams.Length; i++)
        {
            var param = ctorParams[i];
            var matchingProp = type.GetProperties()
                .FirstOrDefault(p => string.Equals(p.Name, param.Name, StringComparison.OrdinalIgnoreCase));

            _ctorBinders[i] = new ConstructorBinder(
                CreateBinderForMember(param, matchingProp, routeSegments, visited));
        }

        RequiresValidation = ParameterValidator.HasValidationAttributes(type);
    }

    public override async ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
    {
        var args = new object?[_ctorBinders.Length];
        for (var i = 0; i < _ctorBinders.Length; i++)
        {
            args[i] = await _ctorBinders[i].Binder.BindAsync(ctx, services);
        }

        return Activator.CreateInstance(_type, args);
    }

    internal bool RequiresValidation { get; }

    private static ParameterBinder CreateBinderForMember(
        ParameterInfo param,
        PropertyInfo? prop,
        HashSet<string> routeSegments,
        HashSet<Type> visited)
    {
        // Collect attributes from both property and constructor parameter
        var attrs = prop is not null
            ? prop.GetCustomAttributes(true).Concat(param.GetCustomAttributes(true)).ToArray()
            : param.GetCustomAttributes(true);

        var type = param.ParameterType;
        var name = param.Name!;

        foreach (var attr in attrs)
        {
            if (attr is FromRouteAttribute fromRoute)
            {
                return new RouteValueBinder(fromRoute.Name ?? name, type);
            }

            if (attr is FromQueryAttribute fromQuery)
            {
                return new QueryStringBinder(fromQuery.Name ?? name, type);
            }

            if (attr is FromHeaderAttribute fromHeader)
            {
                return new HeaderBinder(fromHeader.Name ?? name, type);
            }

            if (attr is FromBodyAttribute)
            {
                return new JsonBodyBinder(type);
            }

            if (attr is FromFormAttribute fromForm)
            {
                if (type == typeof(IFormFile))
                {
                    return new FormFileBinder(fromForm.Name ?? name);
                }

                return new FormBinder(fromForm.Name ?? name, type);
            }

            if (attr is FromServicesAttribute)
            {
                return new ServiceBinder(type);
            }

            if (attr is AsParametersAttribute)
            {
                return new AsParametersBinder(type, routeSegments, [..visited]);
            }
        }

        // Convention fallback
        if (type == typeof(TurboHttpContext))
        {
            return new ContextBinder();
        }

        if (type == typeof(CancellationToken))
        {
            return new CancellationTokenBinder();
        }

        if (routeSegments.Contains(name))
        {
            return new RouteValueBinder(name, type);
        }

        if (type == typeof(IFormFile) || type == typeof(IFormFileCollection))
        {
            return new FormFileBinder(name);
        }

        if (type.IsInterface || (type.IsClass && type != typeof(string)))
        {
            return new ServiceBinder(type);
        }

        return new QueryStringBinder(name, type);
    }

    private sealed class ConstructorBinder(ParameterBinder binder)
    {
        public ParameterBinder Binder { get; } = binder;
    }
}