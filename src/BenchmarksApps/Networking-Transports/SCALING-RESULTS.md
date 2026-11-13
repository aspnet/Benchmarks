# Networking-Transports: what limits the Linux server numbers

The throughput tables are in [GOLD-RESULTS.md](GOLD-RESULTS.md) (56-core machines), [LINUX-RESULTS.md](LINUX-RESULTS.md) and [WINDOWS-RESULTS.md](WINDOWS-RESULTS.md) (small machines). This file holds the measurements that explain them: what each request costs in CPU, where the network receive work runs, whether the load machine is the limit, and how to run one cell. The architecture and the API are described in `src/Servers/Kestrel/Transport.Networking/Bedrock.md` and `Bedrock-API.md` in the aspnetcore branch `dmkorolev/project-bedrock` of DeagleGross/aspnetcore.

All numbers are as measured on crank (`aspnet-gold-lin-relay` unless stated, runtime `11.0.0-rtm.26480.113`, TCP HTTP/1.1 keep-alive, 1 KB response) and have not been independently reproduced. The CPU-per-request and NIC rows are **single runs**. In every table "% of Sockets" means throughput as a fraction of the System.Net.Sockets run in the same table (100% is parity, 120% is 20% more requests per second than Sockets).

## Switches that the results depend on

| Setting | Default | Switch in these apps |
| --- | --- | --- |
| Join pending worker wake-ups (one `eventfd` write per batch of commands instead of one per command) on the Linux providers | on | `NETWORKPROTO_COALESCE=0` restores a wake per command |
| Complete a pending receive on the provider worker instead of queueing the consumer continuation to the thread pool (`NetworkEngineOptions.CompleteReceivesInline`) | off | `NETWORKPROTO_INLINE_INPUT=1` (`Common/NativeTransport.cs`, `BedrockClient/Common.cs`) |
| Inline Sockets baseline: `SocketTransportOptions.UnsafePreferInlineScheduling = true`, plus on Linux `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1` (that variable only applies to the Unix socket engine) | off | `NETWORKPROTO_SOCKETS_INLINE=1` |
| A once-per-second sampler (`Common/ScalingDiagnostics.cs`, and the epoll counters in the transport) | off | `NETWORKPROTO_DIAG=1` |

The sampler prints per-thread-group CPU, context switches, run-queue wait, thread-pool and lock counters, the topology of the allowed CPUs (`DIAG_TOPO`), the CPUs outside the cpuset (`DIAG_OUT`), cumulative `NET_RX` softirq counts inside and outside the cpuset (`DIAG_NETRX`), and each NIC's link speed, queue count and interrupt affinity (`DIAG_NIC`, `DIAG_NICIRQ`). [scripts/analyze-diag.py](scripts/analyze-diag.py) turns a crank log into the per-request figures below; it counts `iowait` as idle, because the idle time before and after the load shows up as `iowait` for io_uring.

## CPU per request

Whole-process CPU divided by requests per second over the steady window of about 27 s (single runs). Thread-pool items per request are the runtime's work-item counter.

| Workers | Configuration | Requests/s | CPU per request | Thread-pool items per request | Allocated per request |
| --- | --- | --- | --- | --- | --- |
| 16 | Sockets default | 1.01M | 14.8 µs | 1.90 | 2 B |
| 16 | Sockets inline | 1.34M | 11.5 µs | 0 | 133 B |
| 16 | epoll default | 1.18M | 12.4 µs | 0.92 | 246 B |
| 16 | io_uring default | 1.18M | 11.5 µs | 0.81 | 218 B |
| 16 | io_uring inline | 1.75M | 8.9 µs | 0 | 249 B |
| 28 | Sockets default | 1.78M | 15.1 µs | 1.94-1.96 | 2 B |
| 28 | Sockets inline | 1.99M | 13.6 µs | 0 | |
| 28 | epoll default | 1.83M | 14.6 µs | 0.97 | 258 B |
| 28 | epoll inline | 2.41M | 11.2 µs | 0 | |
| 28 | io_uring default | 1.82M | 14.5 µs | 0.97 | 259 B |
| 28 | io_uring inline | 2.64M | 10.3 µs | 0 | |
| 56 | Sockets default | 2.22M | 22.6 µs | 1.96 | 3 B |
| 56 | epoll default | 2.40M | 21.7 µs | 0.96 | 256 B |
| 56 | io_uring default | 2.40M | 21.4 µs | 1.05 | 280 B |

The 56 worker rows run two threads per physical core and are not comparable with the others. The io_uring and epoll 56 worker steady windows are 13-14 s, Sockets 28 s. Per-thread-group figures: [results/scaling-2026-11-12/recheck-sampler.txt](results/scaling-2026-11-12/recheck-sampler.txt), [diag-final-shape-w28.txt](results/scaling-2026-11-12/diag-final-shape-w28.txt).

### Default mode has a ceiling

At 28 workers every transport in default mode is CPU-bound at 14-15 µs per request:

| | Sockets default | epoll default | epoll inline |
| --- | --- | --- | --- |
| System time (`recv`, `send`, `epoll_wait`) | 8.4 µs | 8.1 µs | 7.6 µs |
| User time | 6.9 µs | 6.6 µs | 3.6 µs |
| Soft-interrupt share of the allowed cores | 17% | 19% | 23% |

- **About 8 µs per request is kernel TCP work, and it is the same for all transports.** The epoll threads spend 6.8-7.6 µs of system time per request on `recv` and `send`, and the Sockets thread-pool threads 7.9-8.0 µs.
- **The thread-pool hop costs the prototype providers about 3.5 µs per request** (14.7 µs in default mode against 11.2 µs inline for epoll, almost all of it user time) and Sockets about 1.5 µs (15.3 against 13.8 µs). The likely cause is cache locality: with the hop, the connection's state is touched by a different core on each request. This was not profiled. The providers do not hop more often than Sockets: 0.97 thread-pool items per request against 1.94.
- **Both Linux providers already batch.** io_uring runs one ring per worker with `SINGLE_ISSUER` and `DEFER_TASKRUN` and a multishot receive from a provided buffer ring. epoll does 0.14 `epoll_wait` calls and 1.07 events per request, about 7.6 events per wait in default mode (about 17 inline), and 0.07 worker wake-ups per request.
- **Keep-alive is already the best case for a transport.** A `Connection: close` run is not measurable with this load generator (Sockets: 10.6K requests per second, all 896 connections reporting socket errors, load machine at 95% CPU).
- **Allocation:** the providers allocate about 250 B per request (Sockets default: 2 B), and the GC pauses cost 2.4-2.8% of the time at 28 workers. Pooling the three per-receive objects (the page lease, the buffer storage and the input segment) would recover part of that, probably 1-2%, and was not done.

## The cpuset is not a smaller machine

The application is limited to `W` of the 56 logical CPUs with `--application.cpuSet`. The 40 Gbit/s Mellanox NIC (`mlx5_core`) has 56 receive queues, and `/proc/irq/*/smp_affinity_list` shows one completion-queue interrupt per queue, each pinned to a different single CPU, covering 0-55 exactly once. A cpuset of `W` CPUs therefore holds only `W` of the interrupts, and the rest of the receive (`NET_RX`) softirq work runs on CPUs that the process may not use. The machine is one package with 28 physical cores and two SMT threads per core (CPU `i` and `i + 28`), and one L3 cache shared by all CPUs. Cpusets `0-(W-1)` with `W` up to 28 therefore hold one SMT thread of each core.

| Workers | CPUs outside the cpuset | `NET_RX` events handled outside | Softirq CPU per request on the outside CPUs | Softirq share of the allowed CPUs |
| --- | --- | --- | --- | --- |
| 16 Sockets default | 40 | 64% | 1.5 µs | 10.9% |
| 16 io_uring default | 40 | 62% | 1.6 µs | 10.4% |
| 16 Sockets inline | 40 | 67% | 2.5 µs | 12.8% |
| 16 io_uring inline | 40 | 63% | 2.3 µs | 15.3% |
| 28 Sockets default | 28 (the SMT siblings of the busy cores) | 49% | 1.7 µs | 17.9-18.5% |
| 28 io_uring default | 28 | 49% | 2.0 µs | 18.1-18.9% |
| 56 (whole machine) | 0 | 0% | none | 25.0% Sockets, 26.9% epoll, 28.1% io_uring |

- **The margin of a small cpuset is helped by work the application does not pay for.** At 4 and 16 workers most of the receive processing happens outside the cpuset, so the transport's user-space saving is a larger share of what the application's CPUs do. A real 4-core machine would not have that.
- **The 28-worker cpuset is the least favorable layout.** The idle SMT siblings (about 12% softirq, otherwise idle) do half of the receive work, next to busy cores. Total softirq CPU per request is about 4.75 µs for Sockets at 28 workers against about 3.2 µs at 16 workers, and io_uring's CPU per request rises from 11.5 µs (16 workers) to 14.5 µs (28) while Sockets stays at about 15 µs. SMT contention between the busy cores and the softirq on their siblings is a possible cause; it was not isolated.
- **The whole machine is the unconstrained number.** At 56 workers the interrupts land on the application's own CPUs, all CPUs are 92-98% busy, and epoll and io_uring are 108% of Sockets (one run each).
- The 40 Gbit/s link is not the limit: 2.6M responses of 1 KB per second is about 22.7 Gbit/s of response bytes.

## Is the load machine the limit?

Load machine CPU (logical, steady average, `wrk` with 576 connections at 16 workers, 896 at 28 and 1,792 at 56):

| Workers | Sockets default | epoll default | io_uring default | Sockets inline | io_uring inline |
| --- | --- | --- | --- | --- | --- |
| 16 | 34% | 41% | 40% | 44% | 58% |
| 28 | 57% | - | 61% | - | 82% |
| 56 | 69% | 76% | 73% | - | - |

In default mode the load machine is not the limit: it produced 2.40-2.63M requests per second against inline transports on the same hardware, and it is at 34-41% at 16 workers. The load machine has SMT, so its logical CPU use understates its physical limit, and at 82% (inline at 28 workers) the inline rows may be partly client-limited. A client limit is not proven away, only not indicated.

## Small machine

`idna-intel-lin-relay` (4 logical CPUs, the server limited to 2 CPUs with 2 workers, 256 connections, 32 `wrk` threads, 2 runs per cell, application CPUs saturated at about 198% and the load machine at 66-76%):

| Transport | Requests per second (run 1, run 2) | Mean | % of Sockets | CPU per request | System time per request |
| --- | --- | --- | --- | --- | --- |
| Sockets | 67,969, 65,315 | 66.6K | 100% | 30.6 µs | 15.8 µs |
| epoll | 81,411, 89,094 | 85.3K | 128% | 23.0 µs | |
| io_uring | 97,704, 99,670 | 98.7K | 148% | 20.3 µs | 11.1 µs |

Both native transports use much less CPU per request than Sockets on this machine. The mechanism was not isolated. A gold-lin cpuset of 4 workers (2 runs per cell, 256 connections) gave Sockets 313.9K and 311.8K, epoll 383.9K and 392.4K and io_uring 397.2K requests per second (one run), which is about 124% and 127% of Sockets.

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

- The CPU-per-request, softirq and load-machine rows are single runs. The throughput tables have 1 or 2 repetitions per cell (see [GOLD-RESULTS.md](GOLD-RESULTS.md)), and a few percent between repetitions is noise.
- 1 KB keep-alive TCP only; TLS, larger bodies and connection churn were not measured on the 56-core machines.
- `W` workers on `W` cores of a 56-core machine is not the same as a `W`-core machine (see above).
- Inline completion runs the consumer's code on the transport's worker thread. A consumer that blocks stalls the other connections of that worker, which is why it is an option and not the default.
- The Windows inline p99 increase and the Windows Sockets and IOCP default plateau at 28 workers are not explained, and no CPU-per-request figures were taken on Windows.
- Experiments that did not raise default-mode throughput, and the first (superseded) gold-machine numbers, are recorded in `Bedrock-History.md` in the aspnetcore branch, with the raw files in [results/scaling-2026-11-12](results/scaling-2026-11-12) and [results/gold-2026-11-09](results/gold-2026-11-09).
