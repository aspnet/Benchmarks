using Microsoft.AspNetCore.Http.Features;

public static class FeatureCollectionExtensions
{
    public static IHttpRequestFeature GetRequestFeature(this IFeatureCollection features)
    {
        return features.GetRequiredFeature<IHttpRequestFeature>();
    }

    public static IHttpResponseFeature GetResponseFeature(this IFeatureCollection features)
    {
        return features.GetRequiredFeature<IHttpResponseFeature>();
    }

    public static IHttpResponseBodyFeature GetResponseBodyFeature(this IFeatureCollection features)
    {
        return features.GetRequiredFeature<IHttpResponseBodyFeature>();
    }

    public static TFeature GetRequiredFeature<TFeature>(this IFeatureCollection features)
    {
        return features.Get<TFeature>() ?? throw new InvalidOperationException($"Feature of type {typeof(TFeature).Name} not found");
    }
}
