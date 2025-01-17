using System.Security.Authentication;
using System.Text;
using Microsoft.Win32;

namespace HttpSys;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "benchmark only runs on windows")]
public static class RegistryController
{
    private const string TLS12Key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server";
    private const string TLS13Key = @"SYSTEM\CurrentControlSet\Services\HTTP\Parameters";

    public static void ShowRegistryKeys()
    {
        var tls12Enabled = GetRegistryValue(TLS12Key, "");
        var tls13Enabled = GetRegistryValue(TLS13Key, "EnableHTTP3");

        var strBuilder = new StringBuilder("Registry TLS settings: \n");
        strBuilder.AppendLine("\tTLS 1.2: " + tls12Enabled?.ToString());
        strBuilder.AppendLine("\tTLS 1.3: " + tls13Enabled?.ToString());
        strBuilder.AppendLine("\t------");

        Console.WriteLine(strBuilder.ToString());
    }

    public static void EnableTls(SslProtocols sslProtocols)
    {
        if (sslProtocols.HasFlag(SslProtocols.Tls12))
        {
            EnableTls12();
            return;
        }
        if (sslProtocols.HasFlag(SslProtocols.Tls13))
        {
            EnableTls13();
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(sslProtocols), "Unsupported TLS protocol version. Only TLS1.2 and TLS1.3 are supported.");
    }

    private static void EnableTls12()
    {
        // todo
    }

    private static void EnableTls13()
    {
        var localKey = Environment.Is64BitOperatingSystem
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

        var registrySubKey = localKey.OpenSubKey(TLS13Key, writable: true);
        if (registrySubKey is null)
        {
            Console.WriteLine($"Registry subKey `{TLS13Key}` does not exist. Creating one...");
            registrySubKey = localKey.CreateSubKey(TLS13Key);
            Console.WriteLine($"Created Registry subKey `{TLS13Key}`");
        }

        Console.WriteLine($"Enabling registry setting {TLS13Key}\\EnableHTTP3 for TLS1.3");
        registrySubKey.SetValue("EnableHTTP3", 1);
        Console.WriteLine($"Enabled registry setting {TLS13Key}\\EnableHTTP3 for TLS1.3");
    }

    private static string? GetRegistryValue(string path, string name)
    {
        var localKey = Environment.Is64BitOperatingSystem
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

        var registrySubKey = localKey.OpenSubKey(path);
        if (registrySubKey is not null)
        {
            return registrySubKey.GetValue(name)?.ToString();
        }

        return null;
    }
}
