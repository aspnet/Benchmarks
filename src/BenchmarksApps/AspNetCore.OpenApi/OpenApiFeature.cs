namespace Microsoft.AspNetCore.OpenApi;

public static class OpenApiFeature
{
    /// <summary>
    /// Indicates whether APIs related to OpenAPI/Swagger functionality are enabled.
    /// </summary>
    /// <remarks>
    /// The value of the property is backed by the "Microsoft.AspNetCore.OpenApi.OpenApiFeature.IsEnabled"
    /// <see cref="AppContext"/> setting and defaults to <see langword="true"/> if unset.
    /// </remarks>
    public static bool IsEnabled { get; } =
        AppContext.TryGetSwitch(
            switchName: "Microsoft.AspNetCore.OpenApi.OpenApiFeature.IsEnabled",
            isEnabled: out var value)
        ? value : true;
}
