namespace Microsoft.AspNetCore.Http.Features;

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
}
