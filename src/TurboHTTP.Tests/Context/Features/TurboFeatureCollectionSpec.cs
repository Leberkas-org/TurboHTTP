using System.Net;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Tests.Context.Features;

public sealed class TurboFeatureCollectionSpec
{
    [Fact(Timeout = 5000)]
    public void Get_should_return_null_for_unset_feature()
    {
        var collection = new TurboFeatureCollection();
        Assert.Null(collection.Get<IHttpRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_and_Get_should_round_trip_for_request_feature()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        collection.Set<IHttpRequestFeature>(feature);
        Assert.Same(feature, collection.Get<IHttpRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_and_Get_should_round_trip_for_response_feature()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpResponseFeature();
        collection.Set<IHttpResponseFeature>(feature);
        Assert.Same(feature, collection.Get<IHttpResponseFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_and_Get_should_round_trip_for_connection_feature()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpConnectionFeature
        {
            ConnectionId = "test-connection",
            RemoteIpAddress = IPAddress.Loopback,
            RemotePort = 12345,
            LocalIpAddress = IPAddress.Loopback,
            LocalPort = 80
        };
        collection.Set<IHttpConnectionFeature>(feature);
        Assert.Same(feature, collection.Get<IHttpConnectionFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Set_null_should_clear_feature()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        collection.Set<IHttpRequestFeature>(feature);
        collection.Set<IHttpRequestFeature>(null);
        Assert.Null(collection.Get<IHttpRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void Get_should_fall_back_to_dictionary_for_unknown_types()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TlsHandshakeFeature { Protocol = System.Security.Authentication.SslProtocols.Tls13 };
        collection.Set<ITlsHandshakeFeature>(feature);
        Assert.Same(feature, collection.Get<ITlsHandshakeFeature>());
    }

    [Fact(Timeout = 5000)]
    public void IFeatureCollection_Get_should_work_for_aspnet_interfaces()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        collection.Set<IHttpRequestFeature>(feature);
        IFeatureCollection fc = collection;
        Assert.Same(feature, fc.Get<IHttpRequestFeature>());
    }

    [Fact(Timeout = 5000)]
    public void IFeatureCollection_indexer_should_work()
    {
        var collection = new TurboFeatureCollection();
        var feature = new TurboHttpRequestFeature();
        IFeatureCollection fc = collection;
        fc[typeof(IHttpRequestFeature)] = feature;
        Assert.Same(feature, fc[typeof(IHttpRequestFeature)]);
    }

    [Fact(Timeout = 5000)]
    public void IFeatureCollection_IsReadOnly_should_be_false()
    {
        IFeatureCollection collection = new TurboFeatureCollection();
        Assert.False(collection.IsReadOnly);
    }
}
