using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using HttpSys.NetSh;

namespace HttpSys
{
    public static class NetShWrapper
    {
        public static void DeleteBindingIfExists(string ipPort)
        {
            try
            {
                DeleteBinding(ipPort);
            }
            catch
            {
                // ignore
            }
        }

        public static void DeleteBinding(string ipPort)
        {
            Console.WriteLine("Disabling mTLS for http.sys");

            string command = $"http delete sslcert ipport={ipPort}";
            ExecuteNetShCommand(command);

            Console.WriteLine("Disabled http.sys settings for mTLS");
        }



        public static bool TryGetSslCertBinding(string ipPort, out SslCertBinding result)
        {
            result = new SslCertBinding();

            /*
             Example of output:
             -----------------
                IP:port                      : <ip:port>
                Certificate Hash             : <hash>
                Application ID               : {<guid>}
                Certificate Store Name       : <store-name>
                Verify Client Certificate Revocation : Enabled
                Verify Revocation Using Cached Client Certificate Only : Disabled
                Usage Check                  : Enabled
                Revocation Freshness Time    : 0
                URL Retrieval Timeout        : 0
                Ctl Identifier               : (null)
                Ctl Store Name               : (null)
                DS Mapper Usage              : Disabled
                Negotiate Client Certificate : Disabled
                Reject Connections           : Disabled
                Disable HTTP2                : Not Set
                Disable QUIC                 : Not Set
                Disable TLS1.2               : Not Set
                Disable TLS1.3               : Not Set
                Disable OCSP Stapling        : Not Set
                Enable Token Binding         : Not Set
                Log Extended Events          : Not Set
                Disable Legacy TLS Versions  : Not Set
                Enable Session Ticket        : Not Set
                Disable Session ID           : Not Set
                Enable Caching Client Hello  : Not Set
             Extended Properties:
                PropertyId                   : 0
                Receive Window               : 1048576
             Extended Properties:
                PropertyId                   : 1
                Max Settings Per Frame       : 2796202
                Max Settings Per Minute      : 4294967295
             */
            var bindings = ExecuteNetShCommand($"http show sslcert ipport={ipPort}");
            if (string.IsNullOrEmpty(bindings) || !bindings.Contains(ipPort))
            {
                return false;
            }

            // Extract the certificate thumbprint
            var thumbprintMatch = Regex.Match(bindings, @"Certificate Hash\s+:\s+([a-fA-F0-9]+)");
            if (thumbprintMatch.Success)
            {
                result.CertificateThumbprint = thumbprintMatch.Groups[1].Value;
            }

            // Extract the application ID
            var appIdMatch = Regex.Match(bindings, @"Application ID\s+:\s+{([a-fA-F0-9-]+)}");
            if (appIdMatch.Success)
            {
                result.ApplicationId = appIdMatch.Groups[1].Value;
            }

            var negotiateClientCertEnabledRegex = Regex.Match(bindings, @"Negotiate Client Certificate\s+:\s+([a-zA-Z0-9]+)");
            if (negotiateClientCertEnabledRegex.Success)
            {
                var negotiateClientCertValue = negotiateClientCertEnabledRegex.Groups[1].Value;
                result.NegotiateClientCertificate = IsEnabled(negotiateClientCertValue);
            }

            var disableSessionId = Regex.Match(bindings, @"Disable Session ID\s+:\s+([a-zA-Z0-9 ]+)");
            if (disableSessionId.Success)
            {
                var disableSessionIdValue = disableSessionId.Groups[1].Value;
                result.SessionIdTlsResumptionEnabled = !IsEnabled(disableSessionIdValue);
            }

            var enableSessionTicket = Regex.Match(bindings, @"Enable Session Ticket\s+:\s+([a-zA-Z0-9 ]+)");
            if (enableSessionTicket.Success)
            {
                var enableSessionTicketValue = enableSessionTicket.Groups[1].Value;
                result.SessionTicketTlsResumptionEnabled = IsEnabled(enableSessionTicketValue);
            }

            return true;

            // http will return "Disabled" or "Not Set" for properties which are disabled
            bool IsEnabled(string prop)
            {
                if (prop is "Disabled" or "Not Set") return false;
                return true;
            }
        }

        public static void Show()
        {
            ExecuteNetShCommand("http show sslcert", alwaysLogOutput: true);
        }

        public static void SetTestCertBinding(string ipPort, bool enableClientCertNegotiation)
        {
            Console.WriteLine("Setting up binding for testCert for http.sys");

            var certificate = LoadCertificate();
            Console.WriteLine("Loaded `testCert.pfx` from local file system");
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadWrite);
                store.Add(certificate);
                Console.WriteLine("Added `testCert.pfx` to localMachine cert store");
                store.Close();
            }

            string certThumbprint = certificate.Thumbprint;
            AddCertBinding(ipPort, certThumbprint, enableClientCertNegotiation: enableClientCertNegotiation);

            Console.WriteLine("Configured binding for testCert for http.sys");
        }

        public static bool TrySelfSignCertificate(string ipPort, out string certThumbprint)
        {
            certThumbprint = string.Empty;
            try
            {
                // Extract the IP address from ipPort
                string ipAddress = ipPort.Split(':')[0];

                // Generate a self-signed certificate using PowerShell
                string command = $"New-SelfSignedCertificate -CertStoreLocation cert:\\LocalMachine\\My -DnsName {ipAddress}";
                string output = ExecutePowershellCommand(command);

                // Extract the thumbprint from the output
                var lines = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var lastLine = lines[^1];
                certThumbprint = lastLine.Split(" ", StringSplitOptions.RemoveEmptyEntries)[0];

                Console.WriteLine($"Self-signed certificate for {ipAddress}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to self-sign the certificate: " + ex.Message);
                return false;
            }
        }

        public static void AddCertBinding(
            string ipPort, string certThumbprint,
            string? appId = null,
            bool enableClientCertNegotiation = false,
            bool disablesessionid = true,
            bool enablesessionticket = false)
        => CertBindingCore("add", ipPort, certThumbprint, appId, enableClientCertNegotiation, disablesessionid, enablesessionticket);

        public static void UpdateCertBinding(string ipPort, SslCertBinding binding)
            => UpdateCertBinding(ipPort, binding.CertificateThumbprint, binding.ApplicationId, binding.NegotiateClientCertificate, !binding.SessionIdTlsResumptionEnabled, binding.SessionTicketTlsResumptionEnabled);

        public static void UpdateCertBinding(
            string ipPort, string certThumbprint,
            string? appId = null,
            bool enableClientCertNegotiation = false,
            bool disablesessionid = true,
            bool enablesessionticket = false)
        => CertBindingCore("update", ipPort, certThumbprint, appId, enableClientCertNegotiation, disablesessionid, enablesessionticket);

        private static void CertBindingCore(
            string httpOperation,
            string ipPort, string certThumbprint,
            string? appId = null,
            bool enableClientCertNegotiation = false,
            bool disablesessionid = true,
            bool enablesessionticket = false)
        {
            if (string.IsNullOrEmpty(appId))
            {
                appId = "00000000-0000-0000-0000-000000000000";
            }
            string command = $"http {httpOperation} sslcert ipport={ipPort} certstorename=MY certhash={certThumbprint} appid={{{appId}}} clientcertnegotiation={GetFlagValue(enableClientCertNegotiation)} disablesessionid={GetFlagValue(disablesessionid)} enablesessionticket={GetFlagValue(enablesessionticket)}";
            ExecuteNetShCommand(command);
            Console.WriteLine($"Performed cert binding for {ipPort}");

            string GetFlagValue(bool flag) => flag ? "enable" : "disable";
        }

        private static string ExecutePowershellCommand(string command, bool alwaysLogOutput = false)
            => ExecuteCommand("powershell.exe", command, alwaysLogOutput);

        private static string ExecuteNetShCommand(string command, bool alwaysLogOutput = false)
            => ExecuteCommand("netsh", command, alwaysLogOutput);

        private static string ExecuteCommand(string fileName, string command, bool alwaysLogOutput = false)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(fileName, command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Console.WriteLine($"Executing command: `{fileName} {command}`");
            using Process process = Process.Start(processInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (alwaysLogOutput)
            {
                Console.WriteLine(output);
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{fileName} command execution failure: {output}");
            }

            return output;
        }

        private static X509Certificate2 LoadCertificate()
            => File.Exists("testCert.pfx")
            ? X509CertificateLoader.LoadPkcs12FromFile("testCert.pfx", "testPassword", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable)
            : X509CertificateLoader.LoadPkcs12FromFile("../testCert.pfx", "testPassword", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    }
}
