# Quic Benchmark Apps

Contains a pair of applications for measuring the performance of  and QuicStream.

## Implemented Scenarios

- ReadWrite - Measures the data throughput of the QuicStream class by continuously sending and receiving data.
- Handshake - Measures the time it takes to perform a Quic handshake by repeatedly connecting and disconnecting.
- Rps - Measures the time it takes to perform a 'request' -- from sending data to the server and awaiting exactly receive-buffer-size in response. (todo: separate buffer size from request size)

## Usage

### Common Parameters

- `--receive-buffer-size` - The size of the receive buffer in bytes. Defaults to 32 kB. If 0 is used, the peer will not send any data.
- `--send-buffer-size` - The size of the send buffer in bytes. Defaults to 32 kB. If 0 is used, the endpoint will not send any data (beside a single stream-opening byte in client case).
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
