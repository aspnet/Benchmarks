using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace HttpSys
{
    public static class NetShWrapper
    {
        public static void DisableHttpSysMutualTlsIfExists(string ipPort)
        {
            try
            {
                DisableHttpSysMutualTls(ipPort);
            }
            catch
            {
                // ignore
            }
        }

        public static void DisableHttpSysMutualTls(string ipPort)
        {
            Console.WriteLine("Disabling mTLS for http.sys");

            string command = $"http delete sslcert ipport={ipPort}";
            ExecuteNetShCommand(command);

            Console.WriteLine("Disabled http.sys settings for mTLS");
        }

        public static void Show()
        {
            ExecuteNetShCommand("http show sslcert", alwaysLogOutput: true);
        }

        public static void EnableHttpSysMutualTls(string ipPort)
        {
            Console.WriteLine("Setting up mTLS for http.sys");

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
            string appId = Guid.NewGuid().ToString();

            string command = $"http add sslcert ipport={ipPort} certstorename=MY certhash={certThumbprint} appid={{{appId}}} clientcertnegotiation=enable";
            ExecuteNetShCommand(command);

            Console.WriteLine("Configured http.sys settings for mTLS");
        }

        private static void ExecuteNetShCommand(string command, bool alwaysLogOutput = false)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo("netsh", command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Console.WriteLine($"Executing command: `netsh {command}`");
            using Process process = Process.Start(processInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (alwaysLogOutput)
            {
                Console.WriteLine(output);
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"netsh command execution failure: {output}");
            }
        }

#pragma warning disable SYSLIB0057 // Type or member is obsolete
        private static X509Certificate2 LoadCertificate()
            => File.Exists("testCert.pfx")
            ? X509CertificateLoader.LoadPkcs12FromFile("testCert.pfx", "testPassword", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable)
            : X509CertificateLoader.LoadPkcs12FromFile("../testCert.pfx", "testPassword", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable)
#pragma warning restore SYSLIB0057 // Type or member is obsolete
    }
}
