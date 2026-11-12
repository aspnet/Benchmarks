# Networking-Transports: why Linux lost at 56 cores, and the results after the fix

These results were measured on crank on 2026-11-12. They follow up [GOLD-RESULTS.md](GOLD-RESULTS.md), where the Linux native transports fell behind the Sockets transport at 8 and 28 workers on the 56-core "gold" machines while RIO and IOCP did not. The architecture, the two changes and their API are described in `src/Servers/Kestrel/Transport.Networking/Bedrock.md` and `Bedrock-API.md` in the aspnetcore branch `dmkorolev/project-bedrock` of DeagleGross/aspnetcore.

All numbers are as measured on crank and have not been independently reproduced. In every table "% of Sockets" means throughput as a fraction of the System.Net.Sockets run in the same table (100% is parity, 120% is 20% more requests per second than Sockets).

## What changed between GOLD-RESULTS.md and these runs

| Change | Default | Switch |
| --- | --- | --- |
| Join pending worker wake-ups (one `eventfd` write per batch of commands instead of one per command) on the Linux providers' server side | **on** (it was off for servers) | `NETWORKPROTO_COALESCE=0` restores the old behaviour |
| Complete a pending receive on the provider worker instead of queueing the consumer continuation to the thread pool (`NetworkEngineOptions.CompleteReceivesInline`) | off | `NETWORKPROTO_INLINE_INPUT=1` in these apps (`Common/NativeTransport.cs`, `BedrockClient/Common.cs`) |
| `NetworkInputReader` completes its pending read outside its internal lock | always | none |
| `ConnectionContextStream` zero-length read leaves the data unexamined (the client stall fix) | always | none |

For the Sockets baseline, "inline" means `SocketTransportOptions.UnsafePreferInlineScheduling = true` (`NETWORKPROTO_SOCKETS_INLINE=1`) plus, on Linux, `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1`. That environment variable only applies to the Unix socket engine. `NETWORKPROTO_DIAG=1` turns on a once-per-second sampler (`Common/ScalingDiagnostics.cs`, and the epoll counters in the transport).

## Environment

| Item | Linux | Windows |
| --- | --- | --- |
| Crank profile | `aspnet-gold-lin-relay` | `aspnet-gold-win-relay` |
| Application machine | 56 cores | 56 cores |
| Load machine | separate Linux machine, `wrk` | separate Linux machine, `wrk` |
| Runtime | `11.0.0-rtm.26480.113` | `11.0.0-rtm.26480.113` |
| Registration | Bedrock (`registration=bedrock`) | Bedrock |

The application is limited to `W` cores (`--application.cpuSet 0-(W-1)`) and runs `W` workers, so "4 workers" is four cores of a 56-core machine. TCP, HTTP/1.1 keep-alive, 1 KB response, `wrk` with 256 connections and 32 threads for 4 and 8 workers and 896 connections and 56 threads for 28 workers, 15 s measured window. Each cell is **2 repetitions**; both values are listed. One crank invocation per cell, run in the order repetition, workers, configuration so that drift hits every configuration alike.

The Windows and Linux matrices ran concurrently on different machines. Repetition 1 of the Linux and Windows `inline` rows ran on a build with an environment-variable switch inside the transport. Repetition 2 ran on the build with the public option (except the Linux 4-worker epoll `inline` cell). Repetition 2 is within 0.1-5% of repetition 1 in every inline cell.

## Linux server

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
| 28 | Sockets default | 1,774,934 | 1,791,846 | 1,783,390 | 100% | |
| 28 | Sockets inline | 1,990,877 | 1,979,448 | 1,985,162 | 111% | 100% |
| 28 | epoll default | 1,853,393 | 1,860,734 | 1,857,064 | 104% | 94% |
| 28 | epoll inline | 2,408,393 | 2,434,420 | 2,421,406 | 136% | 122% |
| 28 | io_uring default | 1,835,313 | 1,810,782 | 1,823,048 | 102% | 92% |
| 28 | io_uring inline | 2,600,642 | 2,598,607 | 2,599,625 | 146% | 131% |

p99 latency (ms, mean of 2) at 4 / 8 / 28 workers: Sockets default 4.0 / 3.4 / 3.4, Sockets inline 2.3 / 1.6 / 1.8, epoll default 2.9 / 3.5 / 3.6, epoll inline 1.9 / 1.2 / 1.7, io_uring default 2.8 / 2.0 / 2.8, io_uring inline 1.3 / 1.0 / 1.3.

For comparison, the 2026-11-09 run (default configuration before the changes above, mean of 2) gave epoll 107.8%, 92.7%, 78.1% and io_uring 108.3%, 87.2%, 62.9% of Sockets at 4, 8 and 28 workers.

Raw rows: [results/scaling-2026-11-12/server-linux.csv](results/scaling-2026-11-12/server-linux.csv).

### What the sampler says at 28 workers

Single runs with `NETWORKPROTO_DIAG=1` (per-thread-group CPU from `/proc`, thread-pool and lock counters from the runtime, epoll syscall counters from the transport), steady window of about 27 s. Files: [diag-before-fix-epoll-w28.txt](results/scaling-2026-11-12/diag-before-fix-epoll-w28.txt) and [diag-final-shape-w28.txt](results/scaling-2026-11-12/diag-final-shape-w28.txt).

| Configuration (28 workers) | Requests/s | CPU per request | Thread-pool items per request | Worker wake-ups per request | Run-queue wait (cores) |
| --- | --- | --- | --- | --- | --- |
| Sockets default | 1.78M | 15.1 µs | 1.96 | n/a | 2.3 |
| Sockets inline | 1.99M | 13.6 µs | 0 | n/a | 0.2 |
| epoll, wake-ups not joined (the GOLD-RESULTS build) | 1.41M | 17.7 µs | 0.97 | 2.00 | 18.4 |
| epoll, wake-ups joined | 1.83M | 14.6 µs | 0.97 | 0.07 | 17.2 |
| epoll, joined and inline receive | 2.41M | 11.2 µs | 0 | 0 | 0.5 |
| io_uring, default | 1.82M | 14.5 µs | 0.97 | n/a | 21.6 |
| io_uring, inline receive | 2.64M | 10.3 µs | 0 | n/a | 0.2 |

- The `recv` and `send` system time per request is similar: 6.8-7.6 µs on the epoll threads against 7.9-8.0 µs on the Sockets thread-pool threads.
- Not joining wake-ups added about 3 µs per request (two `eventfd` writes per request from thread-pool threads, and 0.46 `epoll_wait` calls per request against 0.11 once joined), and more runtime lock contention (0.021 contended acquisitions per request against 0.0004).
- In default mode 28 epoll threads and about 40 thread-pool threads share 28 cores. Inline completion removes the thread-pool set and the run-queue wait.
- io_uring is reported now: [scripts/analyze-diag.py](scripts/analyze-diag.py) counts `iowait` as idle (idle time before and after the load shows up as `iowait` for io_uring, which had widened the steady window and diluted the per-request CPU). In default mode io_uring uses 14.5 µs per request against 14.7 µs for epoll.

### Default mode has a ceiling

Single runs on gold-lin at 28 workers, [default-mode-experiments.txt](results/scaling-2026-11-12/default-mode-experiments.txt) (summary), [default-mode-experiments-diag.txt](results/scaling-2026-11-12/default-mode-experiments-diag.txt) (sampler output per cell) and [default-mode-experiments.patch](results/scaling-2026-11-12/default-mode-experiments.patch) (the experimental code, not merged). Sockets control of the same evening: 1.77M requests per second.

- About 8 µs per request is kernel TCP work, the same for Sockets, epoll and inline. The hop to the thread pool costs the Bedrock providers about 3.5 µs per request (14.7 µs in default mode against 11.2 µs inline) and Sockets about 1.5 µs. That is why default mode is only 102-104% of Sockets at 28 workers and inline is 136-146%.
- Sending from the thread that flushes the response and returning pages from the thread that releases them (no commands, no wake-ups): 1.70M, 96% of Sockets. Running `recv`, the application and `send` on a thread-pool thread with an edge-triggered epoll thread: 1.45M, 82%. The same direct send inside inline completion: 2.23M against 2.40M for inline. None of them helped.
- 14 workers lose (epoll 1.66M, io_uring 1.58M, 12-17% of the cores idle), 20 workers equal 28 (1.85M and 1.78M), and `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0` makes no difference (1.75M and 1.72M).
- `Connection: close` is not measurable with this load generator (Sockets: 10.6K requests per second, 896 socket errors, load machine at 95% CPU).
- Bedrock allocates about 258 B per request (Sockets default: 2 B), which costs 2.4-2.8% in GC pauses.

## Windows server

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

p99 latency (ms, mean of 2) at 4 / 28 workers: Sockets default 37.9 / 29.9, Sockets inline 2.1 / 3.3, IOCP default 38.1 / 23.0, IOCP inline 2.1 / 52.5, RIO default 9.1 / 21.5, RIO inline 36.5 / 92.1. **Inline raises p99 for RIO at both worker counts and for IOCP at 28 workers**, unlike Linux. Its cause was not investigated. Sockets and IOCP default at 28 workers are both about 1.40M, which is not a machine limit because RIO inline reaches 2.18M.

Raw rows: [results/scaling-2026-11-12/server-windows.csv](results/scaling-2026-11-12/server-windows.csv).

## Client on small machines

`BedrockClient` (`HttpClient` with the prototype in `SocketsHttpHandler.ConnectCallback`) against a plain Kestrel Sockets server on a Linux machine, 1 KB responses, `idna` crank profiles. The client is limited to `W` cores; 128 connections at 2 cores, 256 at 4; 10 s warmup, 15 s measured; 2 repetitions. 0 errors and 0 bad responses in all 24 runs.

| Client OS | Client cores | Handler | Requests/s (rep 1, rep 2) | Mean | % of Sockets | Machine CPU (client / server) |
| --- | --- | --- | --- | --- | --- | --- |
| Linux | 2 | Sockets | 58,987, 64,242 | 61,614 | 100% | 56% / 71% |
| Linux | 2 | epoll | 71,295, 68,651 | 69,973 | 114% | 55% / 66% |
| Linux | 2 | io_uring | 73,287, 72,832 | 73,059 | 119% | 53% / 69% |
| Linux | 4 | Sockets | 99,679, 104,232 | 101,955 | 100% | 93% / 88% |
| Linux | 4 | epoll | 102,085, 104,865 | 103,475 | 101% | 89% / 84% |
| Linux | 4 | io_uring | 103,005, 106,743 | 104,874 | 103% | 88% / 88% |
| Windows (AMD) | 2 | Sockets | 46,730, 55,401 | 51,066 | 100% | 71% / 64% |
| Windows (AMD) | 2 | IOCP | 52,173, 52,828 | 52,500 | 103% | 69% / 65% |
| Windows (AMD) | 2 | RIO | 70,658, 74,136 | 72,397 | 142% | 73% / 65% |
| Windows (AMD) | 4 | Sockets | 84,052, 81,249 | 82,650 | 100% | 90% / 80% |
| Windows (AMD) | 4 | IOCP | 75,337, 74,262 | 74,800 | 91% | 99% / 73% |
| Windows (AMD) | 4 | RIO | 98,714, 98,448 | 98,581 | 119% | 98% / 82% |

At 4 cores both machines are 80-99% busy, so the server limits and the Linux providers converge on Sockets. Sockets at 2 cores on Windows varied by 17% between repetitions. These runs are the first client matrices after the zero-length read fix, so they also serve as its check. Raw rows: [results/scaling-2026-11-12/client-idna-linux.csv](results/scaling-2026-11-12/client-idna-linux.csv) and [results/scaling-2026-11-12/client-idna-windows.csv](results/scaling-2026-11-12/client-idna-windows.csv).

A first Windows client run on a different idna machine was discarded: every handler measured 1.6-4k requests per second with the machine at 91-100% CPU while the client process used 0.3-0.6 cores, which points at something else running on the machine. The Linux client on the 56-core machines was not re-run after the fix.

## Running one cell

The scenario and apps are the ones from [README.md](README.md). A cell is one crank invocation of this shape; the Linux inline epoll cell at 28 workers is:

```
crank --config scenarios/networking-transports.benchmarks.yml
      --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/build/azure.profile.yml
      --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/steadystate.profile.yml
      --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/build/ci.profile.yml
      --scenario epoll-tcp --profile aspnet-gold-lin-relay
      --application.framework net11.0 --application.aspNetCoreVersion 11.0.0-rtm.26480.113
      --application.runtimeVersion 11.0.0-rtm.26480.113 --application.sdkVersion 11.0.100-rtm.26480.113
      --variable forkOwner=aspnet --variable windowsAccept=pool --variable registration=bedrock
      --variable workers=28 --variable size=1024
      --application.cpuSet 0-27 --load.variables.connections 896 --load.variables.threads 56
      --application.source.localFolder <repo copy that contains the prototype DLLs under */libs>
      --application.environmentVariables NETWORKPROTO_INLINE_INPUT=1
      --application.environmentVariables NETWORKPROTO_DIAG=1
      --relay
```

Add `--application.environmentVariables NETWORKPROTO_SOCKETS_INLINE=1` and `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1` for the inline Sockets baseline, and drop the inline switch for the default rows. The DLLs under `*/libs` must come from a build of the aspnetcore branch at or after the commit that added `NetworkEngineOptions.CompleteReceivesInline`.

## Caveats

- Two repetitions per cell (a few percent between them). The diagnostic and default-mode experiment rows are single runs.
- 1 KB keep-alive TCP only; TLS, larger bodies and connection churn were not measured on these machines.
- `W` workers on `W` cores of a 56-core machine is not the same as a `W`-core machine.
- Inline completion runs the consumer's code on the transport's worker thread. A consumer that blocks stalls the other connections of that worker, which is why it is an option and not the default.
- The Windows inline p99 increase and the Windows Sockets and IOCP default plateau at 28 workers are not explained.
- The client matrices on the 56-core machines (GOLD-RESULTS.md) were not repeated, and the idna client matrices have 2 repetitions per cell.
