# Networking-Transports: Windows results on small machines (IOCP and RIO vs stock Sockets)

The 4-vCPU Intel crank machine (`idna-intel-win-relay`), with the original (private-factory) registration, TCP and TLS, plus the client on a small Windows machine. The 56-core machines (the current build, through the Connections abstractions) are in [GOLD-RESULTS.md](GOLD-RESULTS.md). All values are means of 3 runs per cell unless stated, measured on crank, and not independently reproduced.

Sockets, IOCP and all TCP rows were measured on 2026-10-27 (108 runs). The RIO TLS rows (keep-alive and close) were measured on 2026-10-29 (18 runs, 0 bad responses, 0 socket errors) on a build with the RIO TLS close fix. The server matrix below was not re-measured on the current build; the client section was (2026-11-12).

## Environment

| | |
|---|---|
| Crank profile | `idna-intel-win-relay` |
| Server | `IDNA_INTEL`, Windows x64, 4 logical CPUs, no CPU pinning |
| Load generator | `IDNA_AMD`, Linux x64, 4 logical CPUs, wrk |
| Runtime | net11.0, `11.0.0-rtm.26480.113` (SDK `11.0.100-rtm.26480.113`) |
| Prototype | `dmkorolev/network-proto` of the aspnetcore fork, Release build of `src/Servers/Kestrel/Transport.Networking` (RIO TLS rows: the build with the RIO TLS close fix, `b46ed6e964`). Registered through the prototype's own factory, before `CompleteReceivesInline` existed |
| IOCP / RIO | 4 workers, `NETWORKPROTO_WINDOWS_ACCEPT=pool`, TLS owned by the transport (direct SSPI Schannel) |
| Sockets | stock Kestrel Sockets transport, `UseHttps` (SslStream), same certificate, TLS resumption disabled |
| TLS | TLS 1.2 for all transports (`NETWORKPROTO_WINDOWS_TLS=1.2`) |
| Load | wrk, 256 connections, 32 threads, 15 s warmup + 15 s measured, no pipelining, `GET /{size}` returns `size` bytes |
| Keep-alive vs close | keep-alive is the default; close adds `Connection: close` (`presetHeaders=connectionclose`) |
| Payload | 1 KB, 8 KB, 64 KB |
| CPU | server machine-level CPU (`benchmarks/cpu/global`), average of the 15 s steady window |
| RPS / CPU | mean RPS divided by mean machine CPU %, i.e. RPS per 1% of machine CPU |

## RPS

| Mode | Payload | Sockets | IOCP | IOCP vs Sockets | RIO | RIO vs Sockets |
|---|---|---|---|---|---|---|
| TCP keep-alive | 1 KB | 92,397 | 86,069 | -6.8% | 117,070 | +26.7% |
| TCP keep-alive | 8 KB | 68,358 | 68,962 | +0.9% | 84,885 | +24.2% |
| TCP keep-alive | 64 KB | 17,410 | 17,843 | +2.5% | 17,984 | +3.3% |
| TCP close | 1 KB | 1,278 | 1,301 | +1.8% | 1,295 | +1.4% |
| TCP close | 8 KB | 1,309 | 1,312 | +0.3% | 1,293 | -1.2% |
| TCP close | 64 KB | 1,227 | 1,229 | +0.2% | 1,202 | -2.0% |
| TLS keep-alive | 1 KB | 63,146 | 72,637 | +15.0% | 91,992 | +45.7% |
| TLS keep-alive | 8 KB | 41,584 | 52,460 | +26.2% | 68,432 | +64.6% |
| TLS keep-alive | 64 KB | 11,865 | 15,784 | +33.0% | 15,904 | +34.0% |
| TLS close | 1 KB | 243 | 763 | +214.0% | 744 | +206.2% |
| TLS close | 8 KB | 244 | 769 | +214.7% | 746 | +205.5% |
| TLS close | 64 KB | 233 | 697 | +198.4% | 634 | +171.8% |

## RPS per 1% of machine CPU

| Mode | Payload | Sockets CPU | Sockets RPS/CPU | IOCP CPU | IOCP RPS/CPU | IOCP vs Sockets | RIO CPU | RIO RPS/CPU | RIO vs Sockets |
|---|---|---|---|---|---|---|---|---|---|
| TCP keep-alive | 1 KB | 98.0% | 943 | 97.3% | 885 | -6.1% | 97.4% | 1,202 | +27.5% |
| TCP keep-alive | 8 KB | 95.1% | 719 | 98.1% | 703 | -2.2% | 97.4% | 871 | +21.2% |
| TCP keep-alive | 64 KB | 92.9% | 187 | 91.2% | 196 | +4.4% | 87.3% | 206 | +9.8% |
| TCP close | 1 KB | 71.9% | 17.8 | 75.0% | 17.4 | -2.4% | 76.0% | 17.1 | -4.1% |
| TCP close | 8 KB | 72.2% | 18.1 | 73.3% | 17.9 | -1.3% | 75.5% | 17.1 | -5.6% |
| TCP close | 64 KB | 75.7% | 16.2 | 78.7% | 15.6 | -3.7% | 79.0% | 15.2 | -6.2% |
| TLS keep-alive | 1 KB | 96.6% | 653 | 99.1% | 733 | +12.2% | 92.5% | 994 | +52.1% |
| TLS keep-alive | 8 KB | 99.7% | 417 | 96.6% | 543 | +30.2% | 96.9% | 706 | +69.4% |
| TLS keep-alive | 64 KB | 97.0% | 122 | 99.3% | 159 | +29.9% | 96.2% | 165 | +35.1% |
| TLS close | 1 KB | 94.8% | 2.56 | 92.7% | 8.24 | +221.1% | 91.9% | 8.09 | +215.6% |
| TLS close | 8 KB | 96.6% | 2.53 | 97.3% | 7.90 | +212.5% | 96.1% | 7.77 | +207.2% |
| TLS close | 64 KB | 94.2% | 2.48 | 97.6% | 7.13 | +187.8% | 94.0% | 6.75 | +172.2% |

## Notes

1. **RIO TLS rows.** All RIO TLS rows (keep-alive and close, 1/8/64 KB, 3 runs each, 18 runs) are from the 2026-10-29 build with the RIO TLS close fix.
2. **TCP close is not a fair comparison.** The load generator machine is at 99-100% CPU in every close run (TCP and TLS), so the RPS of about 1.3k is limited by the client and is nearly identical across transports. Keep-alive runs leave the client at 59-92%.
3. Sockets TCP keep-alive 1 KB varied 15.6% (CV) between its 3 runs, so the IOCP -6.8% there is within noise.
4. **RIO TLS close** is within -2.5%, -2.9% and -8.9% of IOCP (1, 8, 64 KB), both transports being limited by the saturated load generator (about 99.8% CPU) and the server machine (92-98% CPU).

## Client

`BedrockClient` (an `HttpClient` / `SocketsHttpHandler` application) with the prototype plugged in through `SocketsHttpHandler.ConnectCallback`, on a Windows AMD machine against a plain Kestrel Sockets server on a Linux machine, so only the client transport changes. Measured on 2026-11-12 on the current build (the Bedrock registration), the client limited to `W` cores, 128 connections at 2 cores and 256 at 4, 1 KB responses, 10 s warmup, 15 s measured, 2 runs per cell, 0 errors and 0 bad responses in all 12 Windows runs.

| Client cores | Handler | Requests/s (run 1, run 2) | Mean | % of Sockets | Machine CPU (client / server) |
| --- | --- | --- | --- | --- | --- |
| 2 | Sockets | 46,730, 55,401 | 51,066 | 100% | 71% / 64% |
| 2 | IOCP | 52,173, 52,828 | 52,500 | 103% | 69% / 65% |
| 2 | RIO | 70,658, 74,136 | 72,397 | 142% | 73% / 65% |
| 4 | Sockets | 84,052, 81,249 | 82,650 | 100% | 90% / 80% |
| 4 | IOCP | 75,337, 74,262 | 74,800 | 91% | 99% / 73% |
| 4 | RIO | 98,714, 98,448 | 98,581 | 119% | 98% / 82% |

When the client is the limiting side (2 cores) RIO is clearly ahead and IOCP is at parity. At 4 cores both machines are 80-99% busy, IOCP is behind (91%, as on the server at 4 workers) and RIO stays ahead. Sockets at 2 cores varied by 17% between repetitions, so that cell is noisy. Raw rows: [results/scaling-2026-11-12/client-idna-windows.csv](results/scaling-2026-11-12/client-idna-windows.csv). The Windows client on the 56-core machine is in [GOLD-RESULTS.md](GOLD-RESULTS.md#client-httpclient-with-the-prototype-as-connectcallback) (single runs, an earlier build).
