using System.Text;
using Microsoft.Win32;

namespace HttpSys
{
    public static class RegistryController
    {
        private const string TLS12Key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server";
        private const string TLS13Key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\HTTP\Parameters";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "benchmark only runs on windows")]
        public static void ShowRegistryKeys()
        {
            var strBuilder = new StringBuilder("Registry TLS settings: \n");

            var tls12Enabled = Registry.GetValue(TLS12Key, "Enabled", "not defined");
            strBuilder.AppendLine("\tTLS 1.2: " + tls12Enabled?.ToString());

            var tls13Enabled = Registry.GetValue(TLS13Key, "EnableHTTP3", "not defined");
            strBuilder.AppendLine("\tTLS 1.3: " + tls12Enabled?.ToString());

            Console.WriteLine(strBuilder.ToString());
        }
    }
}
