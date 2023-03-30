using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace TodosApi;

public class JwtHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<JwtBearerOptions> _jwtOptions;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<JwtHealthCheck> _logger;

    public JwtHealthCheck(IOptionsMonitor<JwtBearerOptions> jwtOptions, IHostEnvironment hostEnvironment, ILogger<JwtHealthCheck> logger)
    {
        _jwtOptions = jwtOptions;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var valid = ValidateJwtOptions(_jwtOptions.CurrentValue, _hostEnvironment);
        var status = valid
            ? HealthCheckResult.Healthy("")
            : HealthCheckResult.Degraded("JWT options are not configured. Run 'dotnet user-jwts create' in project directory to configure JWT.");
        return Task.FromResult(status);
    }

    private const string JwtOptionsLogMessage = "JwtBearerAuthentication options configuration: {JwtOptions}";
    
    private bool ValidateJwtOptions(JwtBearerOptions options, IHostEnvironment hostEnvironment)
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
