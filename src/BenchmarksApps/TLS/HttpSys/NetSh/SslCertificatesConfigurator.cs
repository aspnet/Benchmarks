using System.Security.Cryptography.X509Certificates;

namespace HttpSys.NetSh
{
    public static class SslCertificatesConfigurator
    {
        public static void RemoveCertificate(string thumbprint)
        {
            try
            {
                using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite);

                    var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
                    if (certs.Count == 0)
                    {
                        Console.WriteLine("Certificate not found.");
                    }

                    foreach (var cert in certs)
                    {
                        store.Remove(cert);
                        Console.WriteLine($"Deleted certificate (store LocalMachine/My): {cert.Subject}");
                    }
                    store.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Remove certificate (thumbprint='{thumbprint}') error: {ex.Message}");
            }
        }
    }
}
