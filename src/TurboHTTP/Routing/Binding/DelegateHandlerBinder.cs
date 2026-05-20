using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TurboHTTP.Server;

namespace TurboHTTP.Routing.Binding;

internal sealed class BindingValidationException(int statusCode, Dictionary<string, List<string>>? errors = null)
    : Exception("Parameter validation failed.")
{
    public int StatusCode { get; } = statusCode;
    public Dictionary<string, List<string>> Errors { get; } = errors ?? new Dictionary<string, List<string>>();
}

internal static class DelegateHandlerBinder
{
    internal static Func<TurboHttpContext, IServiceProvider, ValueTask<object>> BindEntityDelegate(
        string pattern,
        Delegate? handler)
    {
        var method = handler!.Method;
        var parameters = method.GetParameters();
        var routeSegments = ExtractRouteSegments(pattern);

        var binders = new ParameterBinder[parameters.Length];
        var requiresValidation = new bool[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            binders[i] = CreateBinder(parameters[i], routeSegments);
            if (binders[i] is JsonBodyBinder or FormBinder)
            {
                requiresValidation[i] = ParameterValidator.HasValidationAttributes(parameters[i].ParameterType);
            }
            else if (binders[i] is AsParametersBinder asParams)
            {
                requiresValidation[i] = asParams.RequiresValidation;
            }
        }

        ValidateBinderConfiguration(pattern, parameters);

        return async (ctx, services) =>
        {
            try
            {
                var args = await BindArgs(binders, ctx, services);

                var validationErrors = RunValidation(requiresValidation, args, parameters);
                if (validationErrors is not null)
                {
                    throw new BindingValidationException(400, validationErrors);
                }

                var result = handler.DynamicInvoke(args);
                if (result is Task<object> taskObj)
                {
                    return await taskObj;
                }

                if (result is ValueTask<object> vtObj)
                {
                    return await vtObj;
                }

                return result ?? throw new InvalidOperationException("Entity message factory returned null.");
            }
            catch (ParameterParseException)
            {
                throw new BindingValidationException(400);
            }
        };
    }

    internal static Func<TurboHttpContext, IServiceProvider, Task> Bind(
        string pattern,
        Delegate handler)
    {
        var method = handler.Method;
        var parameters = method.GetParameters();
        var routeSegments = ExtractRouteSegments(pattern);

        var binders = new ParameterBinder[parameters.Length];
        var requiresValidation = new bool[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            binders[i] = CreateBinder(parameters[i], routeSegments);
            if (binders[i] is JsonBodyBinder or FormBinder)
            {
                requiresValidation[i] = ParameterValidator.HasValidationAttributes(parameters[i].ParameterType);
            }
            else if (binders[i] is AsParametersBinder asParams)
            {
                requiresValidation[i] = asParams.RequiresValidation;
            }
        }

        ValidateBinderConfiguration(pattern, parameters);

        var returnType = method.ReturnType;
        var unwrappedType = returnType;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            unwrappedType = returnType.GetGenericArguments()[0];
        }

        if (typeof(IResult).IsAssignableFrom(unwrappedType))
        {
            return CreateIResultHandler(handler, binders, returnType, requiresValidation, parameters);
        }

        throw new InvalidOperationException(
            string.Concat(
                "Handler for '", pattern,
                "' must return IResult or Task<IResult>. Got: ",
                returnType.Name));
    }

    private static Func<TurboHttpContext, IServiceProvider, Task> CreateIResultHandler(
        Delegate handler, ParameterBinder[] binders, Type returnType, bool[] requiresValidation,
        ParameterInfo[] parameters)
    {
        return async (ctx, services) =>
        {
            try
            {
                var args = await BindArgs(binders, ctx, services);

                var validationErrors = RunValidation(requiresValidation, args, parameters);
                if (validationErrors is not null)
                {
                    await ParameterValidator.WriteValidationError(ctx, validationErrors);
                    return;
                }

                var result = handler.DynamicInvoke(args);

                IResult? iresult = null;
                if (result is Task task)
                {
                    await task;
                    if (returnType.IsGenericType)
                    {
                        iresult = task.GetType().GetProperty("Result")!.GetValue(task) as IResult;
                    }
                }
                else
                {
                    iresult = result as IResult;
                }

                if (iresult is null)
                {
                    ctx.Response.StatusCode = 500;
                    return;
                }

                await iresult.ExecuteAsync(ctx);
            }
            catch (ParameterParseException)
            {
                ctx.Response.StatusCode = 400;
            }
        };
    }

    private static Dictionary<string, List<string>>? RunValidation(
        bool[] requiresValidation,
        object?[] args,
        ParameterInfo[] parameters)
    {
        Dictionary<string, List<string>>? allErrors = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (!requiresValidation[i] || args[i] is null)
            {
                continue;
            }

            var result = ParameterValidator.ValidateObject(args[i]!, parameters[i].Name!);
            if (!result.IsValid)
            {
                allErrors ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in result.Errors)
                {
                    allErrors[kv.Key] = kv.Value;
                }
            }
        }

        return allErrors;
    }

    private static async ValueTask<object?[]> BindArgs(ParameterBinder[] binders, TurboHttpContext ctx,
        IServiceProvider services)
    {
        var args = new object?[binders.Length];
        for (var i = 0; i < binders.Length; i++)
        {
            args[i] = await binders[i].BindAsync(ctx, services);
        }

        return args;
    }

    private static void ValidateBinderConfiguration(string pattern, ParameterInfo[] parameters)
    {
        var bodyCount = 0;
        var hasForm = false;
        var hasBody = false;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].GetCustomAttribute<FromBodyAttribute>() is not null)
            {
                bodyCount++;
                hasBody = true;
            }

            if (parameters[i].GetCustomAttribute<FromFormAttribute>() is not null)
            {
                hasForm = true;
            }
        }

        if (bodyCount > 1)
        {
            throw new InvalidOperationException(
                string.Concat("Handler for '", pattern, "' has multiple [FromBody] parameters. Only one is allowed."));
        }

        if (hasBody && hasForm)
        {
            throw new InvalidOperationException(
                string.Concat("Handler for '", pattern,
                    "' has both [FromBody] and [FromForm] parameters. These are mutually exclusive."));
        }
    }

    private static ParameterBinder CreateBinder(ParameterInfo parameter, HashSet<string> routeSegments)
    {
        var type = parameter.ParameterType;
        var name = parameter.Name!;

        if (parameter.GetCustomAttribute<FromRouteAttribute>() is { } fromRoute)
        {
            return new RouteValueBinder(fromRoute.Name ?? name, type);
        }

        if (parameter.GetCustomAttribute<FromQueryAttribute>() is { } fromQuery)
        {
            return new QueryStringBinder(fromQuery.Name ?? name, type);
        }

        if (parameter.GetCustomAttribute<FromHeaderAttribute>() is { } fromHeader)
        {
            return new HeaderBinder(fromHeader.Name ?? name, type);
        }

        if (parameter.GetCustomAttribute<FromBodyAttribute>() is not null)
        {
            return new JsonBodyBinder(type);
        }

        if (parameter.GetCustomAttribute<FromFormAttribute>() is { } fromForm)
        {
            if (type == typeof(IFormFile))
            {
                return new FormFileBinder(fromForm.Name ?? name);
            }

            return new FormBinder(fromForm.Name ?? name, type);
        }

        if (parameter.GetCustomAttribute<FromServicesAttribute>() is not null)
        {
            return new ServiceBinder(type);
        }

        if (parameter.GetCustomAttribute<AsParametersAttribute>() is not null)
        {
            return new AsParametersBinder(type, routeSegments);
        }

        if (type == typeof(TurboHttpContext))
        {
            return new ContextBinder();
        }

        if (type == typeof(CancellationToken))
        {
            return new CancellationTokenBinder();
        }

        if (type == typeof(HttpRequestMessage))
        {
            return new RequestBinder();
        }

        if (type == typeof(HttpContext))
        {
            return new HttpContextBinder();
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

    private static HashSet<string> ExtractRouteSegments(string pattern)
    {
        var segments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = pattern.Split('/');
        foreach (var part in parts)
        {
            if (part.StartsWith('{') && part.EndsWith('}'))
            {
                segments.Add(part[1..^1]);
            }
        }

        return segments;
    }
}