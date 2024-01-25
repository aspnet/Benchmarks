# SslStream Benchmark Apps

Contains a pair of applications for measuring the performance of the SslStream class.

## Implemented Scenarios

- ReadWrite - Measures the data throughput of the SslStream class by continuously sending and receiving data.
- Handshake - Measures the time it takes to perform a TLS handshake by repeatedly connecting and disconnecting.

## Usage

### Common Parameters

- `--receive-buffer-size` - The size of the receive buffer in bytes. Defaults to 32 kB.
- `--send-buffer-size` - The size of the receive buffer in bytes. Defaults to 32 kB. If 0 is used, the peer will not send any data.
- `--tls-version` - The TLS version to use. Defaults to TLS 1.3. Supported values are `1.2` and `1.3`.
- `--allow-tls-resume` - Whether to allow TLS session resumption. Defaults to `true`. [.NET 8+ only]
- `--x509-revocation-check-mode` - The revocation check mode to use. Defaults to `NoCheck`.

### ClientOptions

- `--host` - The host to connect to. Required.
- `--port` - The port to connect to. Defaults to 9998.
- `--cert` - Path to the client certificate file.
- `--cert-password` - Optional password to the client certificate file.
- `--cert-selection` - Method of selecting the client certificate, defaults to `CertContext`
- `--tls-host-name` - Target host name to send in SNI extension, defaults to the value of the `--host` parameter.
- `--scenario` - Scenario to run. Required.
- `--duration` - Duration of the scenario. Defaults to 15 seconds.
- `--warmup` - Warmup duration of the scenario. Defaults to 15 seconds.

### ServerOptions

- `--port` - The port to listen on. Defaults to 9998.
- `--cert` - Path to the server certificate file, if unused, a self-signed certificate will be generated.
- `--cert-password` - Optional password to the server certificate file.
- `--cert-selection` - Method of selecting the client certificate, defaults to `CertContext`.
- `--host-name` - Name for the self-signed certificate, defaults to `contoso.com`.
- `--require-client-cert` - Whether to require client certificate, defaults to `false`.
