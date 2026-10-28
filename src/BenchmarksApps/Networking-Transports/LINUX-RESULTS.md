# Networking-Transports: Linux results (epoll and io_uring vs stock Sockets)

Measured on crank on 2026-10-28, 3 runs per cell (162 runs). The values below are means of the 3 runs. They are not independently reproduced.

## Environment

| | |
|---|---|
| Crank profile | `idna-intel-lin-relay` |
| Server | `IDNA_INTEL`, Linux x64, 4 logical CPUs, server limited to 2 CPUs (`--application.cpuSet 0-1`, `workers=2`) |
| Load generator | `IDNA_AMD`, Linux x64, 4 logical CPUs, wrk |
| Runtime | net11.0, `11.0.0-rtm.26480.113` (SDK `11.0.100-rtm.26480.113`) |
| Prototype | `dmkorolev/network-proto` of the aspnetcore fork, Release build of `src/Servers/Kestrel/Transport.Networking`; native `.so` files built in WSL (Ubuntu 24.04, gcc 13), liburing linked statically |
| TLS | TLS 1.2, `ECDHE-RSA-AES128-GCM-SHA256`, same RSA certificate, session resumption disabled; OpenSSL 3.5.5 on the server |
| Sockets | stock Kestrel Sockets transport, `UseHttps` (SslStream) |
| Load | wrk, 256 connections, 32 threads, 15 s warmup + 15 s measured, no pipelining, `GET /{size}` returns `size` bytes |
| Keep-alive vs close | keep-alive is the default; close adds `Connection: close` (`presetHeaders=connectionclose`) |
| Payload | 1 KB, 8 KB, 64 KB |
| CPU | server machine-level CPU (`benchmarks/cpu/global`), average of the 15 s steady window. Only 2 of 4 CPUs are used, so it is 52-58% in keep-alive and TLS runs |
| RPS / CPU | mean RPS divided by mean machine CPU %, i.e. RPS per 1% of machine CPU |

## Variants

| Column | What it is |
|---|---|
| Sockets | Stock Kestrel Sockets, TLS by SslStream above the transport |
| epoll | Own epoll transport. TCP: non-blocking `recv`/`send` on the pump thread. TLS: same plumbing, fd-bound OpenSSL `SSL_read`/`SSL_write` on readiness |
| epoll + session | epoll with the TLS state machine swapped for the runtime's experimental `TlsSocketSession` (fd-bound), selected with `NETWORKPROTO_LINUX_TLS_IMPL=session` |
| io_uring | Own io_uring transport. TCP: multishot `recv` with provided buffers plus `send` SQEs. TLS (ring/BIO): the ring does all socket I/O, OpenSSL only sees a memory BIO |
| io_uring fd | TLS only. The ring only signals readiness (poll SQEs), OpenSSL does the socket I/O on the fd (`NETWORKPROTO_URING_TLS=fd`) |
| io_uring + session | TLS only. fd-bound `TlsSocketSession`, the ring only signals readiness (`NETWORKPROTO_LINUX_TLS_IMPL=session`) |

`n/a`: variant has no TCP-only mode. Percentages are the change versus Sockets in the same row.

## RPS

| Mode | Payload | Sockets | epoll | epoll + session | io_uring | io_uring fd | io_uring + session |
|---|---|---|---|---|---|---|---|
| TCP keep-alive | 1 KB | 73,005 | 79,490 (+8.9%) | n/a | 89,960 (+23.2%) | n/a | n/a |
| TCP keep-alive | 8 KB | 50,534 | 59,323 (+17.4%) | n/a | 65,548 (+29.7%) | n/a | n/a |
| TCP keep-alive | 64 KB | 14,478 | 17,705 (+22.3%) | n/a | 17,921 (+23.8%) | n/a | n/a |
| TCP close † | 1 KB | 1,487 | 3,577 (+140.6%) | n/a | 3,110 (+109.2%) | n/a | n/a |
| TCP close † | 8 KB | 8,610 | 11,730 (+36.2%) | n/a | 4,130 (-52.0%) | n/a | n/a |
| TCP close † | 64 KB | 2,233 | 2,732 (+22.3%) | n/a | 2,718 (+21.7%) | n/a | n/a |
| TLS keep-alive | 1 KB | 37,238 | 52,782 (+41.7%) | 48,753 (+30.9%) | 57,468 (+54.3%) | 48,765 (+31.0%) | 42,293 (+13.6%) |
| TLS keep-alive | 8 KB | 24,933 | 35,565 (+42.6%) | 34,560 (+38.6%) | 37,198 (+49.2%) | 34,227 (+37.3%) | 32,190 (+29.1%) |
| TLS keep-alive | 64 KB | 6,620 | 9,249 (+39.7%) | 8,960 (+35.4%) | 8,732 (+31.9%) | 8,849 (+33.7%) | 8,945 (+35.1%) |
| TLS close ‡ | 1 KB | 729 | 1,021 (+40.1%) | 981 (+34.7%) | 1,051 (+44.2%) | 1,036 (+42.2%) | 947 (+29.9%) |
| TLS close ‡ | 8 KB | 721 | 980 (+36.0%) | 968 (+34.2%) | 1,038 (+44.1%) | 989 (+37.2%) | 969 (+34.5%) |
| TLS close ‡ | 64 KB | 653 | 933 (+42.9%) | 897 (+37.3%) | 962 (+47.2%) | 936 (+43.3%) | 910 (+39.3%) |

## RPS per 1% of machine CPU

| Mode | Payload | Sockets | epoll | epoll + session | io_uring | io_uring fd | io_uring + session |
|---|---|---|---|---|---|---|---|
| TCP keep-alive | 1 KB | 1,295 | 1,490 (+15.0%) | n/a | 1,595 (+23.2%) | n/a | n/a |
| TCP keep-alive | 8 KB | 894 | 1,033 (+15.6%) | n/a | 1,152 (+28.8%) | n/a | n/a |
| TCP keep-alive | 64 KB | 263 | 314 (+19.2%) | n/a | 309 (+17.3%) | n/a | n/a |
| TCP close † | 1 KB | 60.8 | 101 (+66.9%) | n/a | 94.0 (+54.6%) | n/a | n/a |
| TCP close † | 8 KB | 143 | 181 (+26.5%) | n/a | 114 (-20.6%) | n/a | n/a |
| TCP close † | 64 KB | 62.2 | 69.9 (+12.4%) | n/a | 72.9 (+17.1%) | n/a | n/a |
| TLS keep-alive | 1 KB | 690 | 977 (+41.6%) | 906 (+31.2%) | 1,017 (+47.2%) | 911 (+31.9%) | 799 (+15.8%) |
| TLS keep-alive | 8 KB | 463 | 653 (+41.1%) | 660 (+42.4%) | 671 (+44.9%) | 620 (+33.8%) | 589 (+27.1%) |
| TLS keep-alive | 64 KB | 127 | 170 (+34.0%) | 166 (+30.5%) | 163 (+28.4%) | 168 (+32.1%) | 167 (+31.3%) |
| TLS close ‡ | 1 KB | 13.7 | 19.4 (+42.2%) | 18.5 (+35.4%) | 19.9 (+45.6%) | 19.4 (+41.5%) | 17.8 (+30.3%) |
| TLS close ‡ | 8 KB | 13.7 | 17.7 (+28.5%) | 17.6 (+28.2%) | 19.6 (+42.3%) | 18.5 (+34.4%) | 18.3 (+33.1%) |
| TLS close ‡ | 64 KB | 12.5 | 17.3 (+38.6%) | 15.9 (+27.3%) | 17.7 (+41.2%) | 17.0 (+36.0%) | 17.3 (+38.3%) |

## Where we win

- **TCP keep-alive:** epoll +9% to +22% RPS, io_uring +23% to +30% RPS. RPS/CPU is +15% to +19% for epoll and +17% to +29% for io_uring. The 64 KB gap is smaller for both because the payload copy dominates.
- **TCP close:** no valid conclusion, see note 1.
- **TCP + TLS keep-alive:** every transport variant is +31% to +54% over Sockets at 1 and 8 KB, and +32% to +40% at 64 KB (the one exception is io_uring + session at 1 KB, +14%). RPS/CPU follows the same pattern.
- **TCP + TLS close:** +30% to +47% RPS, indicative only, see note 2.
- The wins are consistent with the design differences (no SslStream, Socket, pipe and ThreadPool hops; `recv`/`send` or OpenSSL directly on the pump thread; ring batching). The cause was not profiled in these crank runs.

## Notes

1. **† TCP close is not a transport comparison.** The load generator is at 84-100% CPU in every TCP close run and the server is not saturated (58-178% of 200% core use). RPS is not monotonic with payload (Sockets 1.5k / 8.6k / 2.2k), so the load generator limits the result. The io_uring 8 KB row (4,130, -52%) is an unexplained outlier and was not investigated.
2. **‡ TLS close:** the server is saturated (about 191-198% of 200%) and the load generator is at about 99.9% in every run, so both sides are saturated. Treat it as indicative, not as capacity.
3. **Keep-alive runs are server-bound:** server core use is 190-198% of 200% and the load generator is at 45-75%.
4. **Noise:** TLS keep-alive 1 KB varies about 5-20% between runs (io_uring ring/BIO ranged 50.3k-62.1k, epoll 50.1k-54.3k), so the differences between the TLS variants at 1 KB are partly noise. The 8 KB epoll row ranged 31.9k-38.7k.
5. **Errors:** no bad responses in any run. Two `epoll + session` TLS close 8 KB runs (r2, r3) had 1 socket error each out of about 14.6k requests. One session run needed a retry after a VPN drop.
6. **TLS paths:** epoll, io_uring and io_uring fd use the transport's own OpenSSL integration. The session columns use `TlsSocketSession` from the runtime prototype (dotnet/runtime#128871). The cost of the session wrapper versus native OpenSSL was not profiled.
7. **Sockets baseline** ignores `workers`. It runs stock Kestrel defaults under the 2-CPU limit.

## Architecture: full TLS transport or TCP + optional TLS layer?

| | Approach | TLS ownership | Variants |
|---|---|---|---|
| A | TCP transport, TLS as a Stream above it | SslStream | Sockets |
| B | Full TCP and full TCP+TLS transport | Inside the transport | epoll (native OpenSSL on the fd), io_uring ring/BIO (ring owns ciphertext) |
| C | TCP transport + optional TLS layer | Layer over the connection fd | epoll + session, io_uring fd, io_uring + session |

Change of each approach versus the closest alternative, TLS keep-alive RPS at 1 / 8 / 64 KB:

| Comparison | 1 KB | 8 KB | 64 KB |
|---|---|---|---|
| epoll + session vs epoll native | -7.6% | -2.8% | -3.1% |
| io_uring fd vs io_uring ring/BIO | -15.1% | -8.0% | +1.3% |
| io_uring + session vs io_uring ring/BIO | -26.4% | -13.5% | +2.4% |
| io_uring + session vs io_uring fd | -13.3% | -5.9% | +1.1% |

In TLS close the same comparisons are within -1% to -10% (indicative only, note 2).

**Conclusion** (this setup only: 2 server CPUs, TLS 1.2, 3 runs per cell):

- **epoll:** TCP + an optional TLS layer is enough. The layer is within 1-8% of the full TLS transport, still +31% to +39% over Sockets, and both do the same `recv`/`send` pattern on the fd. A separate full TLS transport buys little.
- **io_uring:** an fd-bound layer (the ring only signals readiness) loses 8-15% at 1-8 KB keep-alive against ring/BIO and is even at 64 KB. The only measured advantage of a TLS-aware transport is ring-owned ciphertext at small payloads.
- A layer can only match that if its contract takes ciphertext in and out (buffer mode). The fd-bound `TlsSocketSession` cannot. A buffer-mode session over ring-owned buffers on Linux was not tested.
- **Design consequence:** do not build a separate TLS transport per backend. Make TLS an optional layer with two modes, fd-bound (epoll, large payloads) and buffer-based (io_uring, small payloads), and keep the ring/BIO path as the reference point to beat.
