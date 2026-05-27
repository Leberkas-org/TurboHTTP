using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboFeatureCollection : IFeatureCollection
{
    private IHttpRequestFeature? _request;
    private IHttpResponseFeature? _response;
    private IHttpConnectionFeature? _connection;
    private IHttpResponseBodyFeature? _responseBody;
    private TurboRequestBodyFeature? _requestBody;
    private IHttpRequestBodyDetectionFeature? _bodyDetection;
    private IHttpRequestLifetimeFeature? _lifetime;
    private IHttpRequestIdentifierFeature? _identifier;
    private IHttpResponseTrailersFeature? _trailers;
    private IHttpResetFeature? _reset;
    private Dictionary<Type, object>? _extras;
    private int _revision;

    public T? Get<T>() where T : class
    {
        if (typeof(T) == typeof(IHttpRequestFeature))
        {
            return Unsafe.As<T>(_request);
        }

        if (typeof(T) == typeof(IHttpResponseFeature))
        {
            return Unsafe.As<T>(_response);
        }

        if (typeof(T) == typeof(IHttpConnectionFeature))
        {
            return Unsafe.As<T>(_connection);
        }

        if (typeof(T) == typeof(IHttpResponseBodyFeature))
        {
            return Unsafe.As<T>(_responseBody);
        }

        if (typeof(T) == typeof(TurboRequestBodyFeature))
        {
            return Unsafe.As<T>(_requestBody);
        }

        if (typeof(T) == typeof(IHttpRequestBodyDetectionFeature))
        {
            return Unsafe.As<T>(_bodyDetection);
        }

        if (typeof(T) == typeof(IHttpRequestLifetimeFeature))
        {
            return Unsafe.As<T>(_lifetime);
        }

        if (typeof(T) == typeof(IHttpRequestIdentifierFeature))
        {
            return Unsafe.As<T>(_identifier);
        }

        if (typeof(T) == typeof(IHttpResponseTrailersFeature))
        {
            return Unsafe.As<T>(_trailers);
        }

        if (typeof(T) == typeof(IHttpResetFeature))
        {
            return Unsafe.As<T>(_reset);
        }

        return _extras is not null && _extras.TryGetValue(typeof(T), out var val) ? (T)val : null;
    }

    public void Set<T>(T? feature) where T : class
    {
        if (typeof(T) == typeof(IHttpRequestFeature))
        {
            _request = Unsafe.As<IHttpRequestFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpResponseFeature))
        {
            _response = Unsafe.As<IHttpResponseFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpConnectionFeature))
        {
            _connection = Unsafe.As<IHttpConnectionFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpResponseBodyFeature))
        {
            _responseBody = Unsafe.As<IHttpResponseBodyFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(TurboRequestBodyFeature))
        {
            _requestBody = Unsafe.As<TurboRequestBodyFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpRequestBodyDetectionFeature))
        {
            _bodyDetection = Unsafe.As<IHttpRequestBodyDetectionFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpRequestLifetimeFeature))
        {
            _lifetime = Unsafe.As<IHttpRequestLifetimeFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpRequestIdentifierFeature))
        {
            _identifier = Unsafe.As<IHttpRequestIdentifierFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpResponseTrailersFeature))
        {
            _trailers = Unsafe.As<IHttpResponseTrailersFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(IHttpResetFeature))
        {
            _reset = Unsafe.As<IHttpResetFeature>(feature);
            _revision++;
            return;
        }

        if (feature is null)
        {
            _extras?.Remove(typeof(T));
        }
        else
        {
            _extras ??= new Dictionary<Type, object>();
            _extras[typeof(T)] = feature;
        }

        _revision++;
    }

    bool IFeatureCollection.IsReadOnly => false;
    int IFeatureCollection.Revision => _revision;

    object? IFeatureCollection.this[Type key]
    {
        get => _extras is not null && _extras.TryGetValue(key, out var val) ? val : null;
        set
        {
            if (value is null)
            {
                _extras?.Remove(key);
            }
            else
            {
                _extras ??= new Dictionary<Type, object>();
                _extras[key] = value;
            }

            _revision++;
        }
    }

    TFeature? IFeatureCollection.Get<TFeature>() where TFeature : default
    {
        if (typeof(TFeature).IsValueType)
        {
            return default;
        }

        var result = GetCore(typeof(TFeature));
        return (TFeature?)result;
    }

    void IFeatureCollection.Set<TFeature>(TFeature? instance) where TFeature : default
    {
        if (typeof(TFeature).IsValueType)
        {
            return;
        }

        SetCore(typeof(TFeature), instance);
    }

    private object? GetCore(Type type)
    {
        if (type == typeof(IHttpRequestFeature))
        {
            return _request;
        }

        if (type == typeof(IHttpResponseFeature))
        {
            return _response;
        }

        if (type == typeof(IHttpConnectionFeature))
        {
            return _connection;
        }

        if (type == typeof(IHttpResponseBodyFeature))
        {
            return _responseBody;
        }

        if (type == typeof(TurboRequestBodyFeature))
        {
            return _requestBody;
        }

        if (type == typeof(IHttpRequestBodyDetectionFeature))
        {
            return _bodyDetection;
        }

        if (type == typeof(IHttpRequestLifetimeFeature))
        {
            return _lifetime;
        }

        if (type == typeof(IHttpRequestIdentifierFeature))
        {
            return _identifier;
        }

        if (type == typeof(IHttpResponseTrailersFeature))
        {
            return _trailers;
        }

        if (type == typeof(IHttpResetFeature))
        {
            return _reset;
        }

        return _extras is not null && _extras.TryGetValue(type, out var val) ? val : null;
    }

    private void SetCore(Type type, object? instance)
    {
        if (type == typeof(IHttpRequestFeature))
        {
            _request = (IHttpRequestFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpResponseFeature))
        {
            _response = (IHttpResponseFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpConnectionFeature))
        {
            _connection = (IHttpConnectionFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpResponseBodyFeature))
        {
            _responseBody = (IHttpResponseBodyFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(TurboRequestBodyFeature))
        {
            _requestBody = (TurboRequestBodyFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpRequestBodyDetectionFeature))
        {
            _bodyDetection = (IHttpRequestBodyDetectionFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpRequestLifetimeFeature))
        {
            _lifetime = (IHttpRequestLifetimeFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpRequestIdentifierFeature))
        {
            _identifier = (IHttpRequestIdentifierFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpResponseTrailersFeature))
        {
            _trailers = (IHttpResponseTrailersFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(IHttpResetFeature))
        {
            _reset = (IHttpResetFeature?)instance;
            _revision++;
            return;
        }

        if (instance is null)
        {
            _extras?.Remove(type);
        }
        else
        {
            _extras ??= new Dictionary<Type, object>();
            _extras[type] = instance;
        }

        _revision++;
    }

    IEnumerator<KeyValuePair<Type, object>> IEnumerable<KeyValuePair<Type, object>>.GetEnumerator()
    {
        if (_request is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpRequestFeature), _request);
        }

        if (_response is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpResponseFeature), _response);
        }

        if (_connection is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpConnectionFeature), _connection);
        }

        if (_responseBody is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpResponseBodyFeature), _responseBody);
        }

        if (_requestBody is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(TurboRequestBodyFeature), _requestBody);
        }

        if (_bodyDetection is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpRequestBodyDetectionFeature), _bodyDetection);
        }

        if (_lifetime is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpRequestLifetimeFeature), _lifetime);
        }

        if (_identifier is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpRequestIdentifierFeature), _identifier);
        }

        if (_trailers is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpResponseTrailersFeature), _trailers);
        }

        if (_reset is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(IHttpResetFeature), _reset);
        }

        if (_extras is not null)
        {
            foreach (var kv in _extras) yield return kv;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<KeyValuePair<Type, object>>)this).GetEnumerator();
}
