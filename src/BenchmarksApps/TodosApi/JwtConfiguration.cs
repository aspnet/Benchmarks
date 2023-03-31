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

                if (!string.IsNullOrEmpty(jwtKeyMaterialValue))
                {
                    var jwtKeyMaterial = Convert.FromBase64String(jwtKeyMaterialValue);
                    var jwtSigningKey = new SymmetricSecurityKey(jwtKeyMaterial);
                    options.TokenValidationParameters.IssuerSigningKey = jwtSigningKey;
                }
            }
        };
    }
}
