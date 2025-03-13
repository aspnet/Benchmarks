namespace HttpSys.NetSh
{
    public class SslCertBinding
    {
        public string CertificateThumbprint { get; set; }

        public string ApplicationId { get; set; }

        /// <summary>
        /// if mutual TLS is enabled
        /// </summary>
        public bool NegotiateClientCertificate { get; set; }

        public bool SessionIdTlsResumptionEnabled { get; set; }
        public bool SessionTicketTlsResumptionEnabled { get; set; }

        public override string ToString() => $"""
            Parsed NetSh Ssl Certificate Binding Info:
                Certificate thumbprint: {CertificateThumbprint}
                Application ID: {ApplicationId}
                Negotiate client certificate: {NegotiateClientCertificate}
                Session ID TLS resumption enabled: {SessionIdTlsResumptionEnabled}
                Session Ticket TLS resumption enabled: {SessionTicketTlsResumptionEnabled}
            -----
        """;
    }
}
