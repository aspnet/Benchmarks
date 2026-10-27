# Networking-Transports

Kestrel servers that run on the prototype shared `System.Net.Transport` engines (epoll, io_uring, IOCP, RIO) and on the stock Sockets transport, so the transports can be compared on crank with the same HTTP workload: plain TCP vs TCP+TLS, for different response sizes.

The prototype assemblies are not part of this repository. They are supplied to the crank build as files, so this sample is only a thin host and will not build without them.

## What the server does

`GET /{bytes}` returns exactly that many bytes (1 to 262144) with a `Content-Length`; any other path returns 1 KiB. There is no routing, logging or per-request state, so the transport dominates the profile.

| Argument | Default | Meaning |
|---|---|---|
| `--urls` | `http://0.0.0.0:5000` | Only the host and port are used. The scheme is ignored. |
| `--tls` | `false` | `true` enables TLS (see below). |
| `--workers` | `4` | Number of transport worker threads (native projects only). |
| `--cpus` | not set | Comma separated logical CPU ids, one per worker (native projects only). |
| `--cert`, `--key` | `certificates/cert.pem`, `certificates/key.pem` | PEM files for the native TLS engines. |

For the native engines, TLS is owned by the transport and Kestrel receives plaintext, so Kestrel never uses `UseHttps` and the listener stays `http`. The client uses `https`. For the stock Sockets baseline `--tls true` uses Kestrel `UseHttps` with `testCert-2048.pfx`, pinned to the same protocol and cipher as the native engine of that OS (TLS 1.2 + `TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256` on Linux, TLS 1.3 on Windows) with resumption disabled.

All four native engines accept optional TLS through `NetworkNativeTlsOptions`, so every engine has one project with a `--tls` switch instead of separate TCP and TLS projects.

## Layout

```
Common/ServerHost.cs        shared Kestrel host and /{bytes} endpoint
Common/NativeTransport.cs   UseNetworkTransport(engine, optional native TLS)
Common/Common.props         net10.0, certificates from ../TLS/Certificates/2048
Common/Native.props         references to libs/*.dll and copies libs/*.so next to the app
Epoll/ IoUring/ Iocp/ Rio/  one project per engine (Program.cs picks the engine options)
Sockets/                    stock transport baseline (not a prototype engine)
scripts/mixed-sizes.lua     wrk script that cycles through 1K, 4K, 8K, 16K, 64K, 256K
```

The crank scenarios are in `scenarios/networking-transports.benchmarks.yml`: `{epoll,iouring,iocp,rio,sockets}-{tcp,tls}` for wrk (closed loop) and the same names with a `-rate` suffix for wrk2 (open loop).

## Supplying the prototype binaries

Build the prototype (`src/Servers/Kestrel/Transport.Networking`) and pass these files to the crank build so they land in the project's `libs` folder before `dotnet publish` runs:

- `Microsoft.AspNetCore.Server.Kestrel.Transport.Networking.dll` and `NetworkTransport.Primitives.dll` (all engines).
- `libnetworkproto2.so`, `libnetworkprotobio.so` (io_uring) and `libnetworkprotoepoll.so` (epoll). They are produced by the adapter's Linux build and copied next to the DLL.

```
--application.options.buildFiles "C:\prototype\libs\*;libs"
```

The `.so` files link to liburing and OpenSSL 3 (rpath `$HOME/.local/lib` when they were built), so the crank Linux machine needs compatible versions or they must be built there. The DLLs should come from a build that matches the `--application.aspNetCoreVersion` used on crank.

## Running

Linux server (epoll, TLS, 64 KB responses), modeled on the antiforgery command:

```
crank --config D:\code\Benchmarks\scenarios\networking-transports.benchmarks.yml
      --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/build/azure.profile.yml
      --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/steadystate.profile.yml
      --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/build/ci.profile.yml
      --scenario epoll-tls --profile idna-intel-lin-relay --variable size=65536
      --application.framework net11.0 --application.options.collectCounters true
      --application.options.buildFiles "C:\prototype\libs\*;libs"
      --application.aspNetCoreVersion 11.0.0-rtm.26480.113 --application.runtimeVersion 11.0.0-rtm.26480.113 --application.sdkVersion 11.0.100-rtm.26480.113
      --relay
```

Windows server: use `--scenario rio-tcp --profile idna-intel-win-relay`. The load generator (wrk) always runs on a Linux machine.

Useful switches:

- Body size: `--variable size=1024|4096|8192|16384|65536|262144`.
- Mixed sizes in one run: `--load.variables.script https://raw.githubusercontent.com/<owner>/Benchmarks/<branch>/src/BenchmarksApps/Networking-Transports/scripts/mixed-sizes.lua`. An optional `--load.variables.scriptArguments "1024,65536"` selects the sizes.
- Short connections (handshake and accept dominated): `--variable presetHeaders=connectionclose`.
- Client load: `--load.variables.connections 64 --load.variables.threads 8` (the wrk defaults are 256 connections and 32 threads); `--load.variables.rate 20000` for wrk2.
- Workers and CPUs: `--variable workers=4 --variable cpus=0,1,2,3`.
- Windows accept: `--variable windowsAccept=pool` sets `NETWORKPROTO_WINDOWS_ACCEPT=pool` (thread-pool accept) for IOCP and RIO.
- io_uring TLS mode: `--variable uringTls=ring|fd` and `--variable ktls=0|1` (`NETWORKPROTO_URING_TLS`, `NETWORKPROTO_KTLS`).
- Source: the scenario file builds the sample from `https://github.com/{{forkOwner}}/Benchmarks.git` at `{{forkBranch}}`; override with `--variable forkOwner=<owner> --variable forkBranch=<branch>`. The branch must be pushed for crank to clone it.

## Known gaps

- Epoll and io_uring providers currently bind only IPv4 loopback (`IPAddress.IsLoopback` in `Epoll/Transport.cs` and `IoUringTcp/Transport.cs`, `INADDR_LOOPBACK` in both `native.c` files), so they throw `NotSupportedException` when listening on the crank machine address. They need to bind a configurable IPv4 address before they can take load from another machine. The Windows providers bind any IPv4 address.
- The io_uring provider pins worker `i` to CPU `2*i` by default; `NativeTransport` falls back to CPUs `0..workers-1` when the machine has fewer than `2*workers` CPUs.
- TLS versions are fixed by the native engines: OpenSSL TLS 1.2 on Linux, SChannel TLS 1.3 on Windows.
- The wrk2 job from the crank repository does not support Lua scripts, so mixed sizes are only available with wrk. wrk2 needs at least a 15 second duration.
- The samples target net10.0 and are meant to be built with the net11 SDK on the crank machine; they have not been built or run.
