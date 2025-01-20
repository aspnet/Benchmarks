using System.Security.Authentication;
using System.Text;
using Microsoft.Win32;

namespace HttpSys;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "benchmark only runs on windows")]
public static class RegistryController
{
    // see https://learn.microsoft.com/en-us/windows-server/security/tls/tls-registry-settings?tabs=diffie-hellman
    private const string TLS12SubKey = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server";
    private const string TLS13SubKey = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.3\Server";

    private static RegistryKey RootRegistryKey => Environment.Is64BitOperatingSystem
        ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
        : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

    public static void ShowRegistryKeys()
    {
        var tls12DisabledByDefault = GetRegistryValue(TLS12SubKey, "DisabledByDefault");
        var tls12Enabled = GetRegistryValue(TLS12SubKey, "Enabled");

        var tls13DisabledByDefault = GetRegistryValue(TLS13SubKey, "DisabledByDefault");
        var tls13Enabled = GetRegistryValue(TLS13SubKey, "Enabled");

        var strBuilder = new StringBuilder("Registry TLS settings: \n");
        strBuilder.AppendLine($"\tTLS 1.2: DisabledByDefault='{tls12DisabledByDefault}', Enabled='{tls12Enabled}'");
        strBuilder.AppendLine($"\tTLS 1.3: DisabledByDefault='{tls13DisabledByDefault}', Enabled='{tls13Enabled}'");
        strBuilder.AppendLine("\t------");

        Console.WriteLine(strBuilder.ToString());
    }

    public static void EnableTls(SslProtocols sslProtocols)
    {
        Console.WriteLine($"Configuring tls to match {sslProtocols}");

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

        Console.WriteLine("Enabling all TLS - no option specified");
        EnableAll();
    }

    private static void EnableAll()
    {
        // Enable TLS1.2
        SetRegistryValue(TLS12SubKey, "DisabledByDefault", value: 0, valueToOverride: 1);
        SetRegistryValue(TLS12SubKey, "Enabled", value: 1, valueToOverride: 0);

        // Enable TLS1.3
        SetRegistryValue(TLS13SubKey, "DisabledByDefault", value: 0, valueToOverride: 1);
        SetRegistryValue(TLS13SubKey, "Enabled", value: 1, valueToOverride: 0);
    }

    private static void EnableTls12()
    {
        // Enable TLS1.2
        SetRegistryValue(TLS12SubKey, "DisabledByDefault", value: 0, valueToOverride: 1);
        SetRegistryValue(TLS12SubKey, "Enabled", value: 1, valueToOverride: 0);

        // and disable TLS1.3
        SetRegistryValue(TLS13SubKey, "DisabledByDefault", value: 0, valueToOverride: 1);
        SetRegistryValue(TLS13SubKey, "Enabled", value: 0, valueToOverride: 1);
    }

    private static void EnableTls13()
    {
        // Enable TLS1.3
        SetRegistryValue(TLS13SubKey, "DisabledByDefault", value: 0, valueToOverride: 1);
        SetRegistryValue(TLS13SubKey, "Enabled", value: 1, valueToOverride: 0);

        // and disable TLS1.2
        SetRegistryValue(TLS12SubKey, "DisabledByDefault", value: 0, valueToOverride: 1);
        SetRegistryValue(TLS12SubKey, "Enabled", value: 0, valueToOverride: 1);
    }

    private static void SetRegistryValue(string subKey, string name, int value, int valueToOverride)
    {
        var registrySubKey = GetAndCreateSubKey(subKey);

        var registryValue = registrySubKey.GetValue(name) as int?;
        if (registryValue is null || registryValue == valueToOverride)
        {
            Console.WriteLine($"Setting value '{value}' on {subKey}\\{name}");
            registrySubKey.SetValue(name, value);
            Console.WriteLine($"Successfully set value '{value}' on {subKey}\\{name}");
        }
    }

    private static int? GetRegistryValue(string path, string name)
    {
        var localKey = RootRegistryKey;

        var registrySubKey = localKey.OpenSubKey(path);
        if (registrySubKey is not null)
        {
            var value = registrySubKey.GetValue(name);
            return value as int?;
        }

        return null;
    }

    private static RegistryKey GetAndCreateSubKey(string path)
    {
        var parts = path.Split(@"\");
        var localKey = RootRegistryKey;

        RegistryKey? registrySubKey = null;
        var currentPath = parts[0] + @"\" + parts[1];
        var i = 1;
        while (i <= parts.Length)
        {
            registrySubKey = localKey.OpenSubKey(currentPath, writable: true);
            if (registrySubKey is null)
            {
                Console.WriteLine($"Registry subKey `{currentPath}` does not exist. Creating one...");
                registrySubKey = localKey.CreateSubKey(currentPath, writable: true);
                Console.WriteLine($"Created Registry subKey `{currentPath}`");
            }
            currentPath = string.Join(@"\", parts.Take(i++ + 1));
        }

        if (registrySubKey is null || registrySubKey.Name.Substring(localKey.Name.Length + 1) != path)
        {
            throw new ArgumentException($"failed to create registry subKey {path}");
        }

        return registrySubKey;
    }
}
