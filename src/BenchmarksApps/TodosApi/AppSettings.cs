using Microsoft.Extensions.Options;

namespace TodosApi;

internal class AppSettings
{
    public required string ConnectionString { get; set; }

    public string? JwtSigningKey { get; set; }

    public bool SuppressDbInitialization { get; set; }
}

// Change to using ValidateDataAnnotations once https://github.com/dotnet/runtime/issues/77412 is complete
internal class AppSettingsValidator : IValidateOptions<AppSettings>
{
    public ValidateOptionsResult Validate(string? name, AppSettings options)
    {
        if (string.IsNullOrEmpty(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail("""
                Connection string not found.
                If running locally, set the connection string in user secrets for key 'AppSettings:ConnectionString'.
                If running after deployment, set the connection string via the environment variable 'APPSETTINGS__CONNECTIONSTRING'.
                """);
        }

        return ValidateOptionsResult.Success;
    }
}

internal static class AppSettingsExtensions
{
    public static IServiceCollection ConfigureAppSettings(this IServiceCollection services, IConfigurationRoot configurationRoot)
    {
        // Can't use the configuration binding source generator due to bug where it emits non-compiling code right now
        // https://github.com/dotnet/runtime/issues/83600
        services.Configure<AppSettings>(configurationRoot.GetSection(nameof(AppSettings)))
            .AddSingleton<IValidateOptions<AppSettings>, AppSettingsValidator>()
            .AddOptions<AppSettings>()
            .ValidateOnStart();

        // Change to using BindConfiguration once https://github.com/dotnet/runtime/issues/83600 is complete
        //services.AddSingleton<IValidateOptions<AppSettings>, AppSettingsValidator>()
        //    .AddOptions<AppSettings>()
        //    .BindConfiguration(nameof(AppSettings))
        //    .ValidateOnStart();

        // Change to using ValidateDataAnnotations once https://github.com/dotnet/runtime/issues/77412 is complete
        //services.AddOptions<AppSettings>()
        //    .BindConfiguration(nameof(AppSettings))
        //    .ValidateDataAnnotations()
        //    .ValidateOnStart();

        return services;
    }
}
