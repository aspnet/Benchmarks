using System.Security.Authentication;

namespace HttpSys
{
    internal static class ConfigurationHelpers
    {
        public static SslProtocols ParseSslProtocols(string? supportedTlsVersions)
        {
            var protocols = SslProtocols.None;
            if (string.IsNullOrEmpty(supportedTlsVersions))
            {
                return protocols;
            }

            foreach (var version in supportedTlsVersions.Split(','))
            {
                switch (version.Trim().ToLower())
                {
#pragma warning disable SYSLIB0039 // Type or member is obsolete
                    case "tls11":
                        protocols |= SslProtocols.Tls11;
                        break;
#pragma warning restore SYSLIB0039 // Type or member is obsolete
                    case "tls12":
                        protocols |= SslProtocols.Tls12;
                        break;
                    case "tls13":
                        protocols |= SslProtocols.Tls13;
                        break;
                    case "any":
                        return SslProtocols.None;
                    default:
                        throw new ArgumentException($"Unsupported TLS version: {version}");
                }
            }

            return protocols;
        }
    }
}
