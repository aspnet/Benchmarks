using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

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

        public static bool BindingExists(string ipPort, out string certThumbprint, out string appId)
        {
            certThumbprint = string.Empty;
            appId = string.Empty;

            var bindings = ExecuteNetShCommand("http show sslcert");
            if (string.IsNullOrEmpty(bindings) || !bindings.Contains(ipPort))
            {
                return false;
            }

            // Extract the certificate thumbprint
            var thumbprintMatch = Regex.Match(bindings, @"Certificate Hash\s+:\s+([a-fA-F0-9]+)");
            if (thumbprintMatch.Success)
            {
                certThumbprint = thumbprintMatch.Groups[1].Value;
            }

            // Extract the application ID
            var appIdMatch = Regex.Match(bindings, @"Application ID\s+:\s+{([a-fA-F0-9-]+)}");
            if (appIdMatch.Success)
            {
                appId = appIdMatch.Groups[1].Value;
            }

            return true;
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
            SetCertBinding(ipPort, certThumbprint, enableClientCertNegotiation: enableClientCertNegotiation);

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

        public static void SetCertBinding(string ipPort, string certThumbprint, string appId = null, bool enableClientCertNegotiation = false)
        {
            var negotiateClientCert = enableClientCertNegotiation ? "enable" : "disable";
            if (string.IsNullOrEmpty(appId))
            {
                appId = "00000000-0000-0000-0000-000000000000";
            }
            string command = $"http add sslcert ipport={ipPort} certstorename=MY certhash={certThumbprint} appid={{{appId}}} clientcertnegotiation={negotiateClientCert}";
            ExecuteNetShCommand(command);
            Console.WriteLine($"Performed cert bindign for {ipPort}");
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
