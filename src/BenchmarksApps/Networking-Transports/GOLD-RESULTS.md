# Networking-Transports: 56-core (gold) results

Kestrel serving through the prototype transports on the 56-core "gold" crank machines, server and client, with the transports registered through the Connections abstractions ("Project Bedrock", [Bedrock.md](https://github.com/DeagleGross/aspnetcore/blob/dmkorolev/project-bedrock/src/Servers/Kestrel/Transport.Networking/Bedrock.md) in the aspnetcore branch `dmkorolev/project-bedrock` of DeagleGross/aspnetcore). The small-machine results are in [LINUX-RESULTS.md](LINUX-RESULTS.md) and [WINDOWS-RESULTS.md](WINDOWS-RESULTS.md), and the CPU-per-request and NIC measurements that explain these numbers are in [SCALING-RESULTS.md](SCALING-RESULTS.md).

All numbers are as measured on crank and have not been independently reproduced, and the prototype is an experiment, not a shipping component. In every table "% of Sockets" means throughput as a fraction of the System.Net.Sockets run in the same table (100% is parity, 120% is 20% more requests per second than Sockets).

## Environment

| Item | Linux | Windows |
| --- | --- | --- |
| Crank profile | `aspnet-gold-lin-relay` | `aspnet-gold-win-relay` |
| Application machine | 56 cores (28 physical cores with SMT), one 40 Gbit/s NIC with 56 receive queues | 56 cores |
| Load machine | separate Linux machine, `wrk` | separate Linux machine, `wrk` |
| Runtime | `11.0.0-rtm.26480.113` | `11.0.0-rtm.26480.113` |
| Registration | Bedrock (`registration=bedrock`) | Bedrock |

The application is limited to `W` cores (`--application.cpuSet 0-(W-1)`) and runs `W` workers, so "4 workers" is four cores of a 56-core machine and 56 workers is the whole machine. That is not the same as a `W`-core machine; see [SCALING-RESULTS.md](SCALING-RESULTS.md#the-cpuset-is-not-a-smaller-machine).

"default" is the shipping configuration of each transport. "inline" is `CompleteReceivesInline = true` for the prototype providers (`NETWORKPROTO_INLINE_INPUT=1` in these apps) and, for the Sockets baseline, `SocketTransportOptions.UnsafePreferInlineScheduling = true` (`NETWORKPROTO_SOCKETS_INLINE=1`) plus, on Linux, `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1`, so the inline rows compare like with like.

## Server (wrk against a Kestrel server on each transport)

TCP, HTTP/1.1 keep-alive, 1 KB response, `wrk` from the separate load machine, 15 s measured window. One crank invocation per cell (scenario `{epoll,iouring,iocp,rio,sockets}-tcp` in [`scenarios/networking-transports.benchmarks.yml`](../../../scenarios/networking-transports.benchmarks.yml)). Connections and `wrk` threads scale with the worker count: 256/32 for 4 and 8 workers, 576/48 for 16, 896/56 for 28 and 1,792/56 for 56. The 4, 8 and 28 worker cells have **2 repetitions** (both listed); the 16 and 56 worker cells are **single runs**. A dash is a cell that was not run.

### Linux

Requests per second:

| Workers | Configuration | Repetition 1 | Repetition 2 | Mean | % of Sockets default | % of Sockets inline |
| --- | --- | --- | --- | --- | --- | --- |
| 4 | Sockets default | 309,747 | 312,262 | 311,004 | 100% | |
| 4 | Sockets inline | 431,441 | 442,114 | 436,778 | 140% | 100% |
| 4 | epoll default | 390,166 | 375,843 | 383,005 | 123% | 88% |
| 4 | epoll inline | 540,949 | 528,410 | 534,679 | 172% | 122% |
| 4 | io_uring default | 395,128 | 389,949 | 392,538 | 126% | 90% |
| 4 | io_uring inline | 604,434 | 580,919 | 592,676 | 191% | 136% |
| 8 | Sockets default | 548,760 | 530,387 | 539,573 | 100% | |
| 8 | Sockets inline | 770,816 | 757,014 | 763,915 | 142% | 100% |
| 8 | epoll default | 542,031 | 536,288 | 539,160 | 100% | 71% |
| 8 | epoll inline | 848,459 | 889,067 | 868,763 | 161% | 114% |
| 8 | io_uring default | 600,620 | 595,995 | 598,307 | 111% | 78% |
| 8 | io_uring inline | 987,207 | 984,645 | 985,926 | 183% | 129% |
| 16 | Sockets default | 1,007,239 | - | 1,007,239 | 100% | |
| 16 | Sockets inline | 1,342,160 | - | 1,342,160 | 133% | 100% |
| 16 | epoll default | 1,184,912 | - | 1,184,912 | 118% | 88% |
| 16 | io_uring default | 1,181,887 | - | 1,181,887 | 117% | 88% |
| 16 | io_uring inline | 1,750,669 | - | 1,750,669 | 174% | 130% |
| 28 | Sockets default | 1,774,934 | 1,791,846 | 1,783,390 | 100% | |
| 28 | Sockets inline | 1,990,877 | 1,979,448 | 1,985,162 | 111% | 100% |
| 28 | epoll default | 1,853,393 | 1,860,734 | 1,857,064 | 104% | 94% |
| 28 | epoll inline | 2,408,393 | 2,434,420 | 2,421,406 | 136% | 122% |
| 28 | io_uring default | 1,835,313 | 1,810,782 | 1,823,048 | 102% | 92% |
| 28 | io_uring inline | 2,600,642 | 2,598,607 | 2,599,625 | 146% | 131% |
| 56 | Sockets default | 2,222,302 | - | 2,222,302 | 100% | |
| 56 | epoll default | 2,403,503 | - | 2,403,503 | 108% | |
| 56 | io_uring default | 2,398,586 | - | 2,398,586 | 108% | |

p99 latency (ms) at 4 / 8 / 28 workers, mean of 2: Sockets default 4.0 / 3.4 / 3.4, Sockets inline 2.3 / 1.6 / 1.8, epoll default 2.9 / 3.5 / 3.6, epoll inline 1.9 / 1.2 / 1.7, io_uring default 2.8 / 2.0 / 2.8, io_uring inline 1.3 / 1.0 / 1.3. At 16 workers (1 run): Sockets 3.3, Sockets inline 1.9, epoll 3.7, io_uring 2.8, io_uring inline 1.3. At 56 workers (1 run): Sockets 3.3, epoll 4.1, io_uring 4.2.

What this shows:

- **Default mode:** epoll and io_uring are at or above Sockets at every worker count, by 0% to 26%. The margin is largest at 4 workers (123% and 126%) and smallest at 28 (104% and 102%), and it is 108% on the whole machine.
- **Inline mode:** the Linux providers are 136-191% of Sockets default and 114-136% of Sockets inline, and they have the lowest p99 of every configuration.
- The 4, 8 and 16 worker rows have receive work running on CPUs outside the cpuset, and the 28 worker row has it on the idle SMT siblings of the busy cores ([SCALING-RESULTS.md](SCALING-RESULTS.md#the-cpuset-is-not-a-smaller-machine)). The 56 worker row is the whole machine.
- At 28 workers and above the load machine is at 57-82% of its logical CPUs, so inline rows may be partly client-limited ([SCALING-RESULTS.md](SCALING-RESULTS.md#is-the-load-machine-the-limit)).

Raw rows: [results/scaling-2026-11-12/server-linux.csv](results/scaling-2026-11-12/server-linux.csv) (4, 8 and 28 workers) and [results/scaling-2026-11-12/recheck-linux.csv](results/scaling-2026-11-12/recheck-linux.csv) (16 and 56 workers, and the small-machine re-check).

### Windows

Requests per second, 4 and 28 workers, `wrk` with 256 and 896 connections:

| Workers | Configuration | Repetition 1 | Repetition 2 | Mean | % of Sockets default |
| --- | --- | --- | --- | --- | --- |
| 4 | Sockets default | 282,310 | 282,554 | 282,432 | 100% |
| 4 | Sockets inline | 286,685 | 286,670 | 286,678 | 102% |
| 4 | IOCP default | 266,856 | 263,103 | 264,980 | 94% |
| 4 | IOCP inline | 319,463 | 318,997 | 319,230 | 113% |
| 4 | RIO default | 327,119 | 324,810 | 325,964 | 115% |
| 4 | RIO inline | 439,809 | 447,438 | 443,623 | 157% |
| 28 | Sockets default | 1,401,514 | 1,421,807 | 1,411,660 | 100% |
| 28 | Sockets inline | 1,403,994 | 1,392,249 | 1,398,121 | 99% |
| 28 | IOCP default | 1,402,736 | 1,411,268 | 1,407,002 | 100% |
| 28 | IOCP inline | 1,711,183 | 1,722,915 | 1,717,049 | 122% |
| 28 | RIO default | 1,702,721 | 1,725,383 | 1,714,052 | 121% |
| 28 | RIO inline | 2,173,234 | 2,183,959 | 2,178,596 | 154% |

p99 latency (ms) at 4 / 28 workers, mean of 2: Sockets default 37.9 / 29.9, Sockets inline 2.1 / 3.3, IOCP default 38.1 / 23.0, IOCP inline 2.1 / 52.5, RIO default 9.1 / 21.5, RIO inline 36.5 / 92.1.

What this shows:

- RIO is ahead of Sockets default at both worker counts (115% and 121%), and inline raises that to 157% and 154%. IOCP is at or below parity in default mode and 113% and 122% inline.
- **Inline raises p99 for RIO at both worker counts and for IOCP at 28 workers**, unlike Linux. The cause was not investigated.
- Sockets and IOCP default at 28 workers are both about 1.40M, which is not a machine limit because RIO inline reaches 2.18M. The cause was not investigated.
- The Sockets inline row uses `UnsafePreferInlineScheduling`; the environment variable above only applies to the Unix socket engine.

Raw rows: [results/scaling-2026-11-12/server-windows.csv](results/scaling-2026-11-12/server-windows.csv).

## Client (HttpClient with the prototype as ConnectCallback)

The load generator is `BedrockClient/` (an `HttpClient` / `SocketsHttpHandler` application). `handler=sockets` is the stock socket layer, and `handler=epoll|iouring|iocp|rio` plugs the prototype into `SocketsHttpHandler.ConnectCallback` through `IConnectionFactory`. The server is a plain Kestrel Sockets server on the other machine for every cell, so only the client transport changes. Scenarios `client-lin-tcp` (client on the Linux load machine, server on the application machine) and `client-win-tcp` (client on the Windows application machine, server on the Linux load machine), 1 KB response, 256 connections, 10 s warmup, 15 s measured, the client limited to `W` cores with `--client.cpuSet`.

Each cell is a single run, and only part of the planned matrix completed (7 of 18 runs per OS):

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

Limits of the client numbers:

- Single run per cell, so differences of a few percent are not meaningful.
- These cells were measured before the zero-length read handling of `ConnectionContextStream` was changed (the non-Sockets handlers sometimes stalled until `HttpClient.Timeout` at 28 workers), and the 56-core client matrices were not repeated afterwards. The small-machine client matrices, which were, are in [LINUX-RESULTS.md](LINUX-RESULTS.md#client) and [WINDOWS-RESULTS.md](WINDOWS-RESULTS.md#client).
- Windows Sockets at 28 workers (249,196 requests per second at about 7% client CPU) looks throttled, so ratios at that worker count would be inflated and are not reported.
- Linux io_uring in the crank staging copy used a statically linked native library because the gold Linux machine lacks `liburing.so.2`. The shipping `.csproj` still links dynamically.

## Other related measurements (not on crank)

The raw-TCP client study against a plain C echo server (HTTP and Kestrel removed from the picture) and the local WSL2 / Windows server matrices are in [`Bedrock-History.md`](https://github.com/DeagleGross/aspnetcore/blob/dmkorolev/project-bedrock/src/Servers/Kestrel/Transport.Networking/Bedrock-History.md) in the aspnetcore branch, not repeated here.
