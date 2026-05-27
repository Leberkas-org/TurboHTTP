using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore;

internal static class EntityDelegateComposer
{
    private static readonly MethodInfo DispatchAsyncMethod =
        typeof(EntityDispatcher).GetMethod("DispatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static Delegate Compose(Delegate messageFactory, EntityDispatcher dispatcher)
    {
        var factoryMethod = messageFactory.Method;
        var factoryParams = factoryMethod.GetParameters();

        var userParamExprs = factoryParams
            .Select(p => Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();

        var ctxParam = Expression.Parameter(typeof(HttpContext), "httpContext");

        Expression factoryCall;
        if (messageFactory.Target is not null)
        {
            factoryCall = Expression.Call(
                Expression.Constant(messageFactory.Target),
                factoryMethod,
                userParamExprs);
        }
        else
        {
            factoryCall = Expression.Call(factoryMethod, userParamExprs);
        }

        var messageExpr = factoryMethod.ReturnType == typeof(object)
            ? factoryCall
            : Expression.Convert(factoryCall, typeof(object));

        var dispatchCall = Expression.Call(
            Expression.Constant(dispatcher),
            DispatchAsyncMethod,
            ctxParam,
            messageExpr);

        var allParams = userParamExprs.Append(ctxParam).ToArray();
        var lambda = Expression.Lambda(dispatchCall, allParams);
        return lambda.Compile();
    }
}
