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
            _netshWrapper.DeleteBindingIfExists(httpsIpPort);

            if (_netshWrapper.TryGetSslCertBinding(httpsIpPort, out var sslCertBinding))
            {
                throw new NetshException($"Binding already exists ({httpsIpPort}). It was unable to be deleted, and run can not proceed without proper configuration. SslCertBinding: " + sslCertBinding);
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
            _netshWrapper.AddCertBinding(
                httpsIpPort,
                _certThumbprint,
                disablesessionid: NetShFlag.NotSet,
                enablesessionticket: NetShFlag.NotSet,
                clientCertNegotiation: NetShFlag.NotSet);
        }
    }
}
