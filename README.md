# Benchmarks
A playground for experimenting with different server models.

# Environment
We're using the following physical machines to perform these tests:

| Name | OS | Role | CPU | RAM | NIC | Notes |
| ---- | --- | ---- | --- | --- | --- | ----- |
| perfsvr | Windows Server 2012 R2 | Web Server | [Xeon E5-1650](http://ark.intel.com/products/64601/Intel-Xeon-Processor-E5-1650-12M-Cache-3_20-GHz-0_0-GTs-Intel-QPI) | 32 GB | [Intel 82579LM](http://www.intel.com/content/www/us/en/embedded/products/networking/82579-gigabit-ethernet-connection-family.html) |  |
| perfsvr2 | Ubuntu 14.04 LTS | Web Server & Load Generator | [Xeon E5-1620](http://ark.intel.com/products/64621/Intel-Xeon-Processor-E5-1620-10M-Cache-3_60-GHz-0_0-GTs-Intel-QPI) | 32 GB | [Intel 82579LM](http://www.intel.com/content/www/us/en/embedded/products/networking/82579-gigabit-ethernet-connection-family.html) |  |
| perf02 | Windows Server 2012 R2 | Load Generator | [Xeon W3550](http://ark.intel.com/products/39720/Intel-Xeon-Processor-W3550-8M-Cache-3_06-GHz-4_80-GTs-Intel-QPI) | 24 GB | Broadcom NetXtreme Gigabit Ethernet (BCM5764) } |  |
| perf03 | Ubuntu 14.04 LTS | Load Generator | [Xeon W3550](http://ark.intel.com/products/39720/Intel-Xeon-Processor-W3550-8M-Cache-3_06-GHz-4_80-GTs-Intel-QPI) | 12 GB | Broadcom NetXtreme Gigabit Ethernet (BCM5764) } |  |

The machines are connected to a 5-port [Netgear GS105](http://www.netgear.com/business/products/switches/unmanaged/GS105.aspx) Gigabit switch.

# Load Generation
We're using [wrk](https://github.com/wg/wrk) to generate load from one of our Linux boxes (usually perfsvr2). We also have [WCAT](http://www.iis.net/downloads/community/2007/05/wcat-63-(x64)) set up on perf02 but it as it doesn't support HTTP pipelining we've stopped using it for now.

# Results
For each stack, variations of the load parameters and multiple runs are tested and the highest result is recorded.

| Stack | Server | Scenario | Req/sec | Load Params | Impl | Observations |
| ----- | --- | -------- | -------- | ---------- | ---- | ------------ |
| ASP.NET 4.6 | perfsvr | Plain Text | 65,383 | 8 threads, 512 connections, no pipelining | Generic reusable handler, unused IIS modules removed | CPU is 100%, almost exclusively in user mode |
| IIS Static File (kernel cached) | perfsvr | Plain Text | 276,727 | 8 threads, 512 connections, no pipelining | hello.html containing "HelloWorld" | CPU is 36%, almost exclusively in kernel mode |
| IIS Static File (non-kernel cached) | perfsvr | Plain Text | 231,609 | 8 threads, 512 connections, no pipelining | hello.html containing "HelloWorld" | CPU is 100%, almost exclusively in user mode |
| ASP.NET 5 on WebListener (kernel cached) | perfsvr | Plain Text | 264,117 | 8 threads, 512 connections, no pipelining | Just app.Run() | CPU is 36%, almost exclusively in kernel mode |
| ASP.NET 5 on WebListener (non-kernel cached) | perfsvr | Plain Text | 107,315 | 8 threads, 512 connections, no pipelining | Just app.Run() | CPU is 100%, mostly in user mode |
| ASP.NET 5 on IIS (Helios) (non-kernel cached) | perfsvr | Plain Text | 109,560 | 8 threads, 512 connections, no pipelining | Just app.Run() | CPU is 100%, mostly in user mode |
| NodeJS | perfsvr | Plain Text | 96,558 | 8 threads, 1024 connections, no pipelining | The actual Techempower NodeJS app | CPU is 100%, almost exclusively in user mode |
| NodeJS | perfsvr | Plain Text | 148,934 | 8 threads, 1024 connections, pipelining 15 deep | The actual Techempower NodeJS app | CPU is 100%, almost exclusively in user mode |
| libuv C# | perfsvr | Plain Text | 300,507 | 12 threads, 1024 connections, no pipelining | Simple TCP server, not real HTTP yet | CPU is 54%, mostly in kernel mode |
| libuv C# | perfsvr | Plain Text | 808,995 | 12 threads, 1024 connections, pipelining 15 deep | Simple TCP server, not real HTTP yet | CPU is 43%, mostly in kernel mode, NIC saturated |
