# Networking-Transports: Windows results (IOCP and RIO vs stock Sockets)

Measured on crank on 2026-10-27, 3 runs per cell (108 runs). The values below are means of the 3 runs. They are not independently reproduced.

## Environment

| | |
|---|---|
| Crank profile | `idna-intel-win-relay` |
| Server | `IDNA_INTEL`, Windows x64, 4 logical CPUs, no CPU pinning |
| Load generator | `IDNA_AMD`, Linux x64, 4 logical CPUs, wrk |
| Runtime | net11.0, `11.0.0-rtm.26480.113` (SDK `11.0.100-rtm.26480.113`) |
| Prototype | `dmkorolev/network-proto` of the aspnetcore fork, Release build of `src/Servers/Kestrel/Transport.Networking` |
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
| TLS keep-alive | 1 KB | 63,146 | 72,637 | +15.0% | 90,454 | +43.2% |
| TLS keep-alive | 8 KB | 41,584 | 52,460 | +26.2% | 62,415 | +50.1% |
| TLS keep-alive | 64 KB | 11,865 | 15,784 | +33.0% | 15,925 | +34.2% |
| TLS close | 1 KB | 243 | 763 | +214.0% | invalid (1) | |
| TLS close | 8 KB | 244 | 769 | +214.7% | invalid (1) | |
| TLS close | 64 KB | 233 | 697 | +198.4% | invalid (1) | |

## RPS per 1% of machine CPU

| Mode | Payload | Sockets CPU | Sockets RPS/CPU | IOCP CPU | IOCP RPS/CPU | IOCP vs Sockets | RIO CPU | RIO RPS/CPU | RIO vs Sockets |
|---|---|---|---|---|---|---|---|---|---|
| TCP keep-alive | 1 KB | 98.0% | 943 | 97.3% | 885 | -6.1% | 97.4% | 1,202 | +27.5% |
| TCP keep-alive | 8 KB | 95.1% | 719 | 98.1% | 703 | -2.2% | 97.4% | 871 | +21.2% |
| TCP keep-alive | 64 KB | 92.9% | 187 | 91.2% | 196 | +4.4% | 87.3% | 206 | +9.8% |
| TCP close | 1 KB | 71.9% | 17.8 | 75.0% | 17.4 | -2.4% | 76.0% | 17.1 | -4.1% |
| TCP close | 8 KB | 72.2% | 18.1 | 73.3% | 17.9 | -1.3% | 75.5% | 17.1 | -5.6% |
| TCP close | 64 KB | 75.7% | 16.2 | 78.7% | 15.6 | -3.7% | 79.0% | 15.2 | -6.2% |
| TLS keep-alive | 1 KB | 96.6% | 653 | 99.1% | 733 | +12.2% | 96.5% | 937 | +43.4% |
| TLS keep-alive | 8 KB | 99.7% | 417 | 96.6% | 543 | +30.2% | 99.2% | 629 | +50.9% |
| TLS keep-alive | 64 KB | 97.0% | 122 | 99.3% | 159 | +29.9% | 96.4% | 165 | +35.0% |
| TLS close | 1 KB | 94.8% | 2.56 | 92.7% | 8.24 | +221.1% | invalid (1) | | |
| TLS close | 8 KB | 96.6% | 2.53 | 97.3% | 7.90 | +212.5% | invalid (1) | | |
| TLS close | 64 KB | 94.2% | 2.48 | 97.6% | 7.13 | +187.8% | invalid (1) | | |

## Notes

1. **Invalid rows.** The RIO TLS close rows (about 25 RPS) were measured on a build that had a RIO TLS close stall bug, which has since been fixed, so they are excluded. A single run after the fix (8 KB, TLS 1.2, close, 2026-10-28) gave 661 RPS at 97.9% machine CPU (6.75 RPS per 1% CPU). It is not part of the means above.
2. **TCP close is not a fair comparison.** The load generator machine is at 99-100% CPU in every close run (TCP and TLS), so the RPS of about 1.3k is limited by the client and is nearly identical across transports. Keep-alive runs leave the client at 59-92%.
3. Sockets TCP keep-alive 1 KB varied 15.6% (CV) between its 3 runs, so the IOCP -6.8% there is within noise.
