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
