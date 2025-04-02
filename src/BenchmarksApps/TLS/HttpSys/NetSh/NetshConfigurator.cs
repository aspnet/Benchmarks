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
                SslCertificatesConfigurator.RemoveCertificate(sslCertBinding.CertificateThumbprint);
                _netshWrapper.DeleteBindingIfExists(httpsIpPort);
            }

            if (!_netshWrapper.TrySelfSignCertificate(httpsIpPort, certPublicKeyLength, out _certThumbprint))
            {
                throw new ApplicationException($"Failed to setup ssl binding for '{httpsIpPort}'.");
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

        public static void LogCurrentSslCertBinding(string httpsIpPort)
            => _netshWrapper.LogSslCertBinding(httpsIpPort);

        public static void ResetNetshConfiguration(
            string httpsIpPort,
            int certPublicKeyLength = 4096)
        {
            // delete cert binding and cert itself. We want it to be as clean and deterministic as possible (even if more actions are performed)
            _netshWrapper.DeleteBindingIfExists(httpsIpPort);
            SslCertificatesConfigurator.RemoveCertificate(_certThumbprint);

            if (!_netshWrapper.TrySelfSignCertificate(httpsIpPort, certPublicKeyLength, out _certThumbprint))
            {
                throw new ApplicationException($"Failed to self-sign a cert for '{httpsIpPort}'.");
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
