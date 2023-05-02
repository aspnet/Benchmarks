using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodosApi;

namespace Microsoft.AspNetCore.Builder;

internal class JwtConfiguration : IConfigureOptions<JwtBearerOptions>
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly AppSettings _appSettings;

    public JwtConfiguration(IHostEnvironment hostEnvironment, IOptions<AppSettings> appSettings)
    {
        _hostEnvironment = hostEnvironment;
        _appSettings = appSettings.Value;
    }

    public void Configure(JwtBearerOptions options)
    {
        if (!_hostEnvironment.IsDevelopment())
        {
            // When not running in development configure the JWT signing key from environment variable
            var jwtKeyMaterialValue = _appSettings.JwtSigningKey;

            if (!string.IsNullOrEmpty(jwtKeyMaterialValue))
            {
                var jwtKeyMaterial = Convert.FromBase64String(jwtKeyMaterialValue);
                var jwtSigningKey = new SymmetricSecurityKey(jwtKeyMaterial);
                options.TokenValidationParameters.IssuerSigningKey = jwtSigningKey;
            }
        }
    }
}
