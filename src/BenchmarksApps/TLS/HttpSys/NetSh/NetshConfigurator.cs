namespace HttpSys.NetSh
{
    public static class NetshConfigurator
    {
        private static readonly NetShWrapper _netshWrapper = new();
        private static string _certThumbprint;

        public static SslCertBinding PreConfigureNetsh(
            string httpsIpPort,
            int certPublicKeyLength = 2048,
            NetShFlag clientCertNegotiation = NetShFlag.Disabled,
            NetShFlag disablesessionid = NetShFlag.Enable,
            NetShFlag enableSessionTicket = NetShFlag.Disabled)
        {
            // we will anyway reconfigure the netsh certificate binding, so we can delete it firstly
            // and also delete a certificate which is bound to the netsh
            if (_netshWrapper.TryGetSslCertBinding(httpsIpPort, out var sslCertBinding))
            {
                Console.WriteLine($"Deleting certificate (thumbprint='{sslCertBinding.CertificateThumbprint}') from the localmachine(my) store");
                SslCertificatesConfigurator.RemoveCertificate(sslCertBinding.CertificateThumbprint);
                _netshWrapper.DeleteBindingIfExists(httpsIpPort);
            }

            if (!_netshWrapper.TrySelfSignCertificate(httpsIpPort, certPublicKeyLength, out _certThumbprint))
            {
                throw new ApplicationException($"Failed to setup ssl binding for '{httpsIpPort}'. Please unblock the VM.");
            }

            _netshWrapper.AddCertBinding(
                httpsIpPort,
                _certThumbprint,
                disablesessionid: disablesessionid,
                enablesessionticket: enableSessionTicket,
                clientCertNegotiation: clientCertNegotiation);

            if (!_netshWrapper.TryGetSslCertBinding(httpsIpPort, out sslCertBinding))
            {
                throw new NetshException($"Failed to setup ssl binding for '{httpsIpPort}'. Please unblock the VM.");
            }

            return sslCertBinding;
        }

        public static void LogCurrentSslCertBinding(string httpsIpPort) => _netshWrapper.LogSslCertBinding(httpsIpPort);

        public static void ResetNetshConfiguration(
            string httpsIpPort,
            int certPublicKeyLength = 4096)
        {
            _netshWrapper.DeleteBindingIfExists(httpsIpPort);
            if (!string.IsNullOrEmpty(_certThumbprint))
            {
                Console.WriteLine($"Deleting certificate (thumbprint='{_certThumbprint}') from the localmachine(my) store");
                SslCertificatesConfigurator.RemoveCertificate(_certThumbprint);
            }

            _netshWrapper.AddCertBinding(
                httpsIpPort,
                _certThumbprint,
                disablesessionid: NetShFlag.NotSet,
                enablesessionticket: NetShFlag.NotSet,
                clientCertNegotiation: NetShFlag.NotSet);
        }
    }
}
