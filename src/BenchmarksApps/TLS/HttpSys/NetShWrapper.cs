﻿using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using HttpSys.NetSh;

namespace HttpSys
{
    public class NetShWrapper
    {
        public bool SupportsDisableSessionId { get; }
        public bool SupportsEnableSessionTicket { get; }

        public NetShWrapper()
        {
            var sslCertCapabilitiesText = ExecuteNetShCommand($"http add sslcert help");
            if (string.IsNullOrEmpty(sslCertCapabilitiesText))
            {
                throw new InvalidOperationException("Failed to determine http.sys capabilities");
            }

            if (sslCertCapabilitiesText.Contains("disablesessionid"))
            {
                SupportsDisableSessionId = true;
            }

            if (sslCertCapabilitiesText.Contains("enablesessionticket"))
            {
                SupportsEnableSessionTicket = true;
            }

            Console.WriteLine($"""
                Http.SYS Capabilities:
                    - SupportsDisableSessionId: {SupportsDisableSessionId} (if not supported, renegotiation will most likely be enabled by default)
                    - SupportsEnableSessionTicket: {SupportsEnableSessionTicket}
            """);
        }

        public void DeleteBindingIfExists(string ipPort)
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

        public void DeleteBinding(string ipPort)
        {
            Console.WriteLine("Disabling mTLS for http.sys");

            string command = $"http delete sslcert ipport={ipPort}";
            ExecuteNetShCommand(command);

            Console.WriteLine("Disabled http.sys settings for mTLS");
        }

        public bool TryGetSslCertBinding(string ipPort, out SslCertBinding result)
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
            var bindings = ExecuteNetShCommand($"http show sslcert ipport={ipPort}", ignoreErrorExit: true);
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
                result.NegotiateClientCertificate = ParseNetShFlag(negotiateClientCertValue);
            }

            var disableSessionId = Regex.Match(bindings, @"Disable Session ID\s+:\s+([a-zA-Z0-9 ]+)");
            if (disableSessionId.Success)
            {
                var disableSessionIdValue = disableSessionId.Groups[1].Value;
                result.DisableSessionIdTlsResumption = ParseNetShFlag(disableSessionIdValue);
            }

            var enableSessionTicket = Regex.Match(bindings, @"Enable Session Ticket\s+:\s+([a-zA-Z0-9 ]+)");
            if (enableSessionTicket.Success)
            {
                var enableSessionTicketValue = enableSessionTicket.Groups[1].Value;
                result.EnableSessionTicketTlsResumption = ParseNetShFlag(enableSessionTicketValue);
            }

            return true;

            NetShFlag ParseNetShFlag(string prop) => prop switch
            {
                "Not Set" => NetShFlag.NotSet,
                "Disable" or "Disabled" => NetShFlag.Disabled,
                "Enable" or "Enabled" or "Set" => NetShFlag.Enable,
                _ => throw new ArgumentOutOfRangeException(nameof(prop), $"unexpected netsh flag '{prop}' for ssl cert binding"),
            };
        }

        public void LogSslCertBinding(string ipPort)
        {
            ExecuteNetShCommand($"http show sslcert ipport={ipPort}", alwaysLogOutput: true);
        }

        public void SetTestCertBinding(string ipPort, bool enableClientCertNegotiation)
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
            AddCertBinding(ipPort, certThumbprint, clientCertNegotiation: enableClientCertNegotiation ? NetShFlag.Enable : NetShFlag.Disabled);

            Console.WriteLine("Configured binding for testCert for http.sys");
        }

        public bool TrySelfSignCertificate(string ipPort, out string certThumbprint)
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

        public void AddCertBinding(
            string ipPort, string certThumbprint,
            string? appId = null,
            NetShFlag clientCertNegotiation = NetShFlag.Disabled,
            NetShFlag disablesessionid = NetShFlag.Enable,
            NetShFlag enablesessionticket = NetShFlag.Disabled)
        => CertBindingCore("add", ipPort, certThumbprint, appId, clientCertNegotiation, disablesessionid, enablesessionticket);

        public void UpdateCertBinding(string ipPort, SslCertBinding binding) => UpdateCertBinding(
            ipPort,
            binding.CertificateThumbprint,
            binding.ApplicationId,
            binding.NegotiateClientCertificate ,
            binding.DisableSessionIdTlsResumption,
            binding.EnableSessionTicketTlsResumption);

        public void UpdateCertBinding(
            string ipPort, string certThumbprint,
            string? appId = null,
            NetShFlag clientCertNegotiation = NetShFlag.Disabled,
            NetShFlag disablesessionid = NetShFlag.Enable,
            NetShFlag enablesessionticket = NetShFlag.Disabled)
        => CertBindingCore("update", ipPort, certThumbprint, appId, clientCertNegotiation, disablesessionid, enablesessionticket);

        private void CertBindingCore(
            string httpOperation,
            string ipPort, string certThumbprint,
            string? appId = null,
            NetShFlag clientcertnegotiation = NetShFlag.Disabled,
            NetShFlag disablesessionid = NetShFlag.Enable,
            NetShFlag enablesessionticket = NetShFlag.Disabled)
        {
            if (string.IsNullOrEmpty(appId))
            {
                appId = "00000000-0000-0000-0000-000000000000";
            }

            var clientcertnegotiationFlag = GetFlagValue(clientcertnegotiation);
            var disablesessionidFlag = GetFlagValue(disablesessionid);
            var enablesessionticketFlag = GetFlagValue(enablesessionticket);
            string command = $"http {httpOperation} sslcert ipport={ipPort} certstorename=MY certhash={certThumbprint} appid={{{appId}}}";

            if (clientcertnegotiationFlag != null)
            {
                command += $" clientcertnegotiation={clientcertnegotiationFlag}";
            }

            // below options are supported only in later versions of HTTP.SYS
            // you can identify if it is available by running `netsh http add sslcert help`

            if (SupportsDisableSessionId && disablesessionidFlag != null)
            {
                command += $" disablesessionid={disablesessionidFlag}";
            }
            if (SupportsEnableSessionTicket && enablesessionticketFlag != null)
            {
                command += $" enablesessionticket={enablesessionticketFlag}";
            }

            ExecuteNetShCommand(command, alwaysLogOutput: true);
            Console.WriteLine($"Performed cert binding for {ipPort}");

            string? GetFlagValue(NetShFlag flag) => flag switch
            {
                NetShFlag.NotSet => null,
                NetShFlag.Disabled => "disable",
                NetShFlag.Enable => "enable",
                _ => throw new ArgumentOutOfRangeException(nameof(flag)),
            };
        }

        private static string ExecutePowershellCommand(string command, bool ignoreErrorExit = false, bool alwaysLogOutput = false)
            => ExecuteCommand("powershell.exe", command, ignoreErrorExit, alwaysLogOutput);

        private static string ExecuteNetShCommand(string command, bool ignoreErrorExit = false, bool alwaysLogOutput = false)
            => ExecuteCommand("netsh", command, ignoreErrorExit, alwaysLogOutput);

        private static string ExecuteCommand(string fileName, string command, bool ignoreErrorExit = false, bool logOutput = false)
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

            if (logOutput)
            {
                Console.WriteLine(output);
            }

            if (!ignoreErrorExit && process.ExitCode != 0)
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
