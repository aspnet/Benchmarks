using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using JsonWebToken;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Mvc
{
    public class CustomJwtAuth : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly SymmetricJwk _key = new SymmetricJwk(Convert.FromBase64String("MFswDQYJKoZIhvcNAQEBBQADSgAwRwJAca32BtkpByiveJTwINuEerWBg2kac7sb"));
        private JwtReader _reader;
        private TokenValidationPolicy _policy;

        public CustomJwtAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
        {
        _policy = new TokenValidationPolicyBuilder()
                       .RequireSignature(_key, SignatureAlgorithm.HmacSha256)
                       .RequireAudience("test")
                       .RequireIssuer("Test")
                       .Build();
         _reader = new JwtReader(_key);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authorization = Request.Headers[HeaderNames.Authorization];
            //var authorization = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJUZXN0IiwiaWF0IjoxNjA3NDc1MDcwLCJleHAiOjE2MzkwMTEwNzQsImF1ZCI6InRlc3QiLCJzdWIiOiJ0ZXN0QHRlc3QuY29tIiwiaHR0cDovL3NjaGVtYXMueG1sc29hcC5vcmcvd3MvMjAwNS8wNS9pZGVudGl0eS9jbGFpbXMvbmFtZSI6InRlc3QiLCJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9lbWFpbCI6InRlc3RAdGVzdC5jb20ifQ.6PYYUPlpSa3Qo8JedZyK8gnqEHVs75SQLu3Sga0kJsk";

            // If no authorization header found, nothing to process further
            if (string.IsNullOrEmpty(authorization))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var result = _reader.TryReadToken(authorization, _policy);

            if (result.Succedeed)
            {
                var claimsIdentity = new ClaimsIdentity("Jwt");
                var payload = result.Token.Payload;
                claimsIdentity.AddClaim(new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/email", (string)payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/email"]));
                claimsIdentity.AddClaim(new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", (string)payload[ClaimTypes.Name]));

                var principal = new ClaimsPrincipal(claimsIdentity);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Jwt")));
            }
            else
            {
                throw new Exception("Failed");
            }
        }
    }
}
