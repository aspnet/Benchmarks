using Microsoft.Extensions.Options;

namespace TodosApi;

internal class AppSettings
{
    public required string ConnectionString { get; set; }
    public string? JwtSigningKey { get; set; }
}

internal class AppSettingsValidator : IValidateOptions<AppSettings>
{
    public ValidateOptionsResult Validate(string? name, AppSettings options)
    {
        if (!string.IsNullOrEmpty(options.ConnectionString))
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
        services.Configure<AppSettings>(configurationRoot.GetSection(nameof(AppSettings)))
            .AddSingleton<IValidateOptions<AppSettings>, AppSettingsValidator>()
            .AddOptions<AppSettings>()
            // TODO: The following methods aren't currently supported by the config binder source generator
            //.BindConfiguration(nameof(AppSettings))
            //.Configure(appSettings => builder.Configuration.Bind(nameof(AppSettings), appSettings))
            .ValidateOnStart();
        return services;
    }
}
