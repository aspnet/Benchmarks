namespace HttpSys.NetSh
{
    public class SslCertBinding
    {
        public string CertificateThumbprint { get; set; }

        public string ApplicationId { get; set; }

        /// <summary>
        /// if mutual TLS is enabled
        /// </summary>
        public NetShFlag NegotiateClientCertificate { get; set; }

        public NetShFlag DisableSessionIdTlsResumption { get; set; }
        public NetShFlag EnableSessionTicketTlsResumption { get; set; }

        public override string ToString() => $"""
            Certificate thumbprint: {CertificateThumbprint}
            Application ID: {ApplicationId}
            Negotiate client certificate: {NegotiateClientCertificate}
            Disable Session ID TLS Resumption: {DisableSessionIdTlsResumption}
            Enable Session Ticket TLS Resumption: {EnableSessionTicketTlsResumption}
            -----
        """;
    }

    [Flags]
    public enum NetShFlag
    {
        NotSet      = 0,

        Disabled    = 1,
        Enable      = 2,
    }
}
