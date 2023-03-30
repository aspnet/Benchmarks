using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.AspNetCore.Builder;

internal static class JwtConfiguration
{
    /// <summary>
    /// Configures JWT Bearer to load the signing key from an environment variable when not running in Development.
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Thrown when the signing key is not found in non-Development environments.</exception>
    public static Action<JwtBearerOptions> ConfigureJwtBearer(WebApplicationBuilder builder)
    {
        return options =>
        {
            if (!builder.Environment.IsDevelopment())
            {
                // When not running in development configure the JWT signing key from environment variable
                var jwtKeyMaterialValue = builder.Configuration["JWT_SIGNING_KEY"];

                if (string.IsNullOrEmpty(jwtKeyMaterialValue))
                {
                    throw new InvalidOperationException("JWT signing key not found!");
                }

                var jwtKeyMaterial = Convert.FromBase64String(jwtKeyMaterialValue);
                var jwtSigningKey = new SymmetricSecurityKey(jwtKeyMaterial);
                options.TokenValidationParameters.IssuerSigningKey = jwtSigningKey;
            }

            // Validate the JWT options
            builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Validate<IHostEnvironment, ILoggerFactory>(ValidateJwtOptions,
                    "JWT options are not configured. Run 'dotnet user-jwts create' in project directory to configure JWT.")
                .ValidateOnStart();
        };
    }

    private const string JwtOptionsLogMessage = "JwtBearerAuthentication options configuration: {JwtOptions}";

    /// <summary>
    /// Validates that JWT Bearer authentication has been configured correctly.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="hostEnvironment"></param>
    /// <param name="loggerFactory"></param>
    /// <returns><c>true</c> if required JWT Bearer settings are loaded, otherwise <c>false</c>.</returns>
    public static bool ValidateJwtOptions(JwtBearerOptions options, IHostEnvironment hostEnvironment, ILoggerFactory loggerFactory)
    {
        var relevantOptions = new JwtOptionsSummary
        {
            Audience = options.Audience,
            ClaimsIssuer = options.ClaimsIssuer,
            Audiences = options.TokenValidationParameters?.ValidAudiences,
            Issuers = options.TokenValidationParameters?.ValidIssuers,
            IssuerSigningKey = options.TokenValidationParameters?.IssuerSigningKey,
            IssuerSigningKeys = options.TokenValidationParameters?.IssuerSigningKeys
        };

        var logger = loggerFactory.CreateLogger(hostEnvironment.ApplicationName ?? nameof(Program));
        var jwtOptionsJson = JsonSerializer.Serialize(relevantOptions, JwtOptionsJsonSerializerContext.Default.JwtOptionsSummary);

        if ((string.IsNullOrEmpty(relevantOptions.Audience) && relevantOptions.Audiences?.Any() != true)
            || (relevantOptions.ClaimsIssuer is null && relevantOptions.Issuers?.Any() != true)
            || (relevantOptions.IssuerSigningKey is null && relevantOptions.IssuerSigningKeys?.Any() != true))
        {
            logger.LogError(JwtOptionsLogMessage, jwtOptionsJson);
            return false;
        }

        logger.LogInformation(JwtOptionsLogMessage, jwtOptionsJson);
        return true;
    }
}


internal class JwtOptionsSummary
{
    public string? Audience { get; set; }
    public string? ClaimsIssuer { get; set; }
    public IEnumerable<string>? Audiences { get; set; }
    public IEnumerable<string>? Issuers { get; set; }
    public SecurityKey? IssuerSigningKey { get; set; }
    public IEnumerable<SecurityKey>? IssuerSigningKeys { get; set; }
}

[JsonSerializable(typeof(JwtOptionsSummary))]
internal partial class JwtOptionsJsonSerializerContext : JsonSerializerContext
{
}