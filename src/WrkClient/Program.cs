using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Wrk2Client;

namespace WrkClient
{
    class Program
    {
        const string WrkFilename = "./wrk";

        static async Task Main(string[] args)
        {
            Console.WriteLine("WRK Client");
            Console.WriteLine("args: " + string.Join(' ', args));

            using var process = Process.Start("chmod", "+x " + WrkFilename);
            process.WaitForExit();

            await WrkProcess.RunAsync(WrkFilename, args);
        }
    }
}
