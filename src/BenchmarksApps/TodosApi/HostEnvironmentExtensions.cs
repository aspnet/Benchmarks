namespace Microsoft.Extensions.Hosting;

internal static class HostEnvironmentExtensions
{
    public static bool IsBuild(this IHostEnvironment hostEnvironment) => hostEnvironment.IsEnvironment("Build");
}
