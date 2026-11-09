# Networking-Transports: gold-profile results (Bedrock registration, server and client)

These results were measured on crank on the 56-core "gold" machines on 2026-11-09. They complement [LINUX-RESULTS.md](LINUX-RESULTS.md) and [WINDOWS-RESULTS.md](WINDOWS-RESULTS.md), which were measured on 4-vCPU Intel machines with the original (private-factory) registration. Here the transports are registered through the Connections abstractions ("Project Bedrock", see `src/Servers/Kestrel/Transport.Networking/Bedrock.md` in the aspnetcore branch `dmkorolev/project-bedrock` of DeagleGross/aspnetcore), and the client side uses the prototype through `SocketsHttpHandler.ConnectCallback`.

All numbers are as measured on crank. They have not been independently reproduced, and the prototype is an experiment, not a shipping component. In every table "% of Sockets" means throughput as a fraction of the System.Net.Sockets run in the same table (100% is parity, 120% is 20% more requests per second than Sockets).

## Environment

| Item | Linux | Windows |
| --- | --- | --- |
| Crank profile | `aspnet-gold-lin-relay` | `aspnet-gold-win-relay` |
| Application machine | 56 cores (crank reports `cores: 56`) | 56 cores |
| Load machine | separate Linux machine | separate Linux machine |
| Runtime | `11.0.0-rtm.26480.113` | `11.0.0-rtm.26480.113` |
| Registration | `NETWORKPROTO_REGISTRATION=bedrock` (`services.AddNetworkTransport(...)`) | same |

The application is limited to `W` workers and its process is pinned with `--application.cpuSet 0-(W-1)`, so "4 workers" means four cores of a 56-core machine, not a four-core machine.

## Server (wrk against a Kestrel server on each transport)

TCP, HTTP/1.1 keep-alive, 1 KB response, wrk from the separate load machine (connections and threads scaled with the worker count: 256/32 for 4 and 8 workers, 896/56 for 28 workers), 15 s load window, mean of 2 repetitions per cell. Scenario: `{epoll,iouring,iocp,rio,sockets}-tcp` in [`scenarios/networking-transports.benchmarks.yml`](../../../scenarios/networking-transports.benchmarks.yml), driven one crank invocation per cell.

Requests per second (mean of 2):

| Workers | Linux Sockets | Linux epoll | Linux io_uring | Windows Sockets | Windows IOCP | Windows RIO |
| --- | --- | --- | --- | --- | --- | --- |
| 4 | 316,934 | 341,704 | 343,226 | 278,159 | 266,313 | 327,210 |
| 8 | 552,043 | 511,905 | 481,136 | 489,433 | 455,504 | 537,155 |
| 28 | 1,821,788 | 1,422,328 | 1,145,692 | 1,401,173 | 1,416,007 | 1,704,225 |

% of Sockets:

| Workers | Linux epoll | Linux io_uring | Windows IOCP | Windows RIO |
| --- | --- | --- | --- | --- |
| 4 | 107.8% | 108.3% | 95.7% | 117.6% |
| 8 | 92.7% | 87.2% | 93.1% | 109.8% |
| 28 | 78.1% | 62.9% | 101.1% | 121.6% |

Raw per-cell rows (latency percentiles, CPU, working set) are in [results/gold-2026-11-09/server-linux.csv](results/gold-2026-11-09/server-linux.csv) and [results/gold-2026-11-09/server-windows.csv](results/gold-2026-11-09/server-windows.csv).

What this shows:

- On Windows, RIO is ahead of Sockets at every worker count (+10% to +22%), and IOCP is at parity or slightly below.
- On Linux, epoll and io_uring are ahead of Sockets only at 4 workers (about +8%). They fall behind at 8 workers and clearly behind at 28 workers. This is much less favourable than the +9% to +30% measured on the 4-vCPU machines with the original registration.
- The Bedrock adapter is not the cause of the Linux gap. With the legacy registration at 28 workers the numbers are the same within noise (epoll 1,437,679 and io_uring 1,132,181 requests per second, [results/gold-2026-11-09/diag-linux-legacy-registration.csv](results/gold-2026-11-09/diag-linux-legacy-registration.csv)), against 1,422,328 and 1,145,692 with Bedrock.
- Explicit CPU pinning of the native workers (`--cpus`) did not help (epoll 346,083 at 8 workers and 1,216,721 at 28 workers; io_uring 481,971 and 1,145,123; [results/gold-2026-11-09/diag-linux-pinned-cpus.csv](results/gold-2026-11-09/diag-linux-pinned-cpus.csv)).
- The reason the Linux native transports stop scaling between 8 and 28 workers on this machine is not established.

## Client (HttpClient with the prototype as ConnectCallback)

The load generator is `BedrockClient/` (an `HttpClient` / `SocketsHttpHandler` application). `handler=sockets` is the stock socket layer. `handler=epoll|iouring|iocp|rio` plugs the prototype into `SocketsHttpHandler.ConnectCallback` through `IConnectionFactory`. The server is a plain Kestrel Sockets server on the other machine for every cell, so only the client transport changes. Scenarios `client-lin-tcp` (client on the Linux load machine, server on the application machine) and `client-win-tcp` (client on the Windows application machine, server on the Linux load machine), 1 KB response, 256 connections, 10 s warmup, 15 s measured, the client limited to `W` cores with `--client.cpuSet`.

Each cell is a single run (not a mean), and only part of the planned matrix completed (see below).

Requests per second:

| Workers | Linux Sockets | Linux epoll | Linux io_uring | Windows Sockets | Windows IOCP | Windows RIO |
| --- | --- | --- | --- | --- | --- | --- |
| 4 | 260,625 | 293,133 | 311,971 | 236,168 | 182,486 | 252,524 |
| 8 | 276,580 | 424,847 | 373,431 | 264,333 | 267,501 | 400,241 |
| 28 | 753,518 | not completed | not run | 249,196 | not completed | not run |

% of Sockets:

| Workers | Linux epoll | Linux io_uring | Windows IOCP | Windows RIO |
| --- | --- | --- | --- | --- |
| 4 | 112.5% | 119.7% | 77.3% | 106.9% |
| 8 | 153.6% | 135.0% | 101.2% | 151.4% |

Raw rows: [results/gold-2026-11-09/client-linux.csv](results/gold-2026-11-09/client-linux.csv) and [results/gold-2026-11-09/client-windows.csv](results/gold-2026-11-09/client-windows.csv).

Caveats for the client numbers:

- Single run per cell, so differences of a few percent are not meaningful.
- The matrices were stopped after 7 successful runs each (of 18 planned) because non-Sockets handlers sometimes stalled until `HttpClient.Timeout` (for example `epoll-w28-1024-r1` failed three attempts). The stall is rare and has not been root-caused. The leading hypothesis is the zero-byte read handling in `ConnectionContextStream.ReadAsync` (`AdvanceTo(Start, End)` makes the next read wait for new data). A `System.IO.Pipelines` test confirmed that primitive, but the end-to-end HttpClient chain has not been proven, and no fix has been applied.
- Windows Sockets at 28 workers (249,196 requests per second at about 7% client CPU) looks throttled, so ratios at that worker count would be inflated and are intentionally not reported.
- Linux io_uring in the crank staging copy used a statically linked native library because the gold Linux machine lacks `liburing.so.2`. The shipping `.csproj` still links dynamically.

## Other related measurements (not on crank)

The raw-TCP client study against a plain C echo server (HTTP and Kestrel removed from the picture) and the local WSL2 / Windows server matrices are in `Bedrock.md` in the aspnetcore branch, not repeated here.
