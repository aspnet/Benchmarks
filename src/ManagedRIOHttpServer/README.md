# Managed C# Registered IO Http Server
Mostly an exploration into calling the [Winsock high-speed networking Registered I/O extensions](https://msdn.microsoft.com/en-us/library/windows/desktop/ms740642(v=vs.85).aspx)
from managed code.

A variation of the ```NativeHttpServer``` behaviour

# Caution
It runs.

Horribly, **horribly** hacky.

There is precious little documentation about even using RIO from native code; so at the moment its more like a trial and error, 
organically grown testbed of functions to call and their signatures than even a sensibly organised bit of code :(

But looks like it can be done! :)

# How to run

Needs the x64 v4.6 CLR; compile release and run the ```ManagedRIOHttpServer.exe``` in the ```bin\Release``` folder from a command prompt.

Or double click on ```ManagedRIOHttpServer.exe``` from windows explorer.

Listens on port 5000

# Todo
* Potentially higher read thoughput by posting multiple receives at a time to and to allow read buffering 
(application managed rather than Winsock managed for RIO); 
as sends are read dependant in this test - may increase overall throughput.
* Deallocate resources correctly.
* Clean up code.

# About Registered IO

Registered RIO //build/ announce from 2011 
http://channel9.msdn.com/events/Build/BUILD2011/SAC-593T

> Microsoft Windows 8 and Windows Server 2012 introduce new Windows Sockets programming elements.

>A set of high-speed networking extensions are available for increased networking performance with lower latency and jitter. These extensions targeted primarily for server applications use pre-registered data buffers and completion queues to increase performance.

>The following are new Windows Sockets functions added to support Winsock high-speed networking Registered I/O extensions:

https://msdn.microsoft.com/en-us/library/windows/desktop/ms740642(v=vs.85).aspx


