using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TodosApi;

internal class JwtHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<JwtBearerOptions> _jwtOptions;
    private readonly ILogger<JwtHealthCheck> _logger;

    public JwtHealthCheck(IOptionsMonitor<JwtBearerOptions> jwtOptions, ILogger<JwtHealthCheck> logger)
    {
        _jwtOptions = jwtOptions;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var valid = ValidateJwtOptions(_jwtOptions.CurrentValue);
        var status = valid
            ? HealthCheckResult.Healthy("JWT options configured correctly")
            : HealthCheckResult.Degraded("JWT options are not configured. Verify the JWT signing key is correctly configured for the current environment.");
        return Task.FromResult(status);
    }

    private const string JwtOptionsLogMessage = "JwtBearerAuthentication options configuration: {JwtOptions}";
    
    private bool ValidateJwtOptions(JwtBearerOptions options)
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
        
        var jwtOptionsJson = JsonSerializer.Serialize(relevantOptions, JwtOptionsJsonSerializerContext.Default.JwtOptionsSummary);

        if ((string.IsNullOrEmpty(relevantOptions.Audience) && relevantOptions.Audiences?.Any() != true)
            || (relevantOptions.ClaimsIssuer is null && relevantOptions.Issuers?.Any() != true)
            || (relevantOptions.IssuerSigningKey is null && relevantOptions.IssuerSigningKeys?.Any() != true))
        {
            _logger.LogWarning(JwtOptionsLogMessage, jwtOptionsJson);
            return false;
        }

        _logger.LogInformation(JwtOptionsLogMessage, jwtOptionsJson);
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
