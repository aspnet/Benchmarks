using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WrkClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("wrk Client");
            Console.WriteLine("args: " + String.Join(' ', args));

            var wrkFilename = "./wrk";
            Process.Start("chmod", "+x " + wrkFilename);

            var process = Process.Start(wrkFilename, String.Join(' ', args));
            
            process.WaitForExit();
        }
    }
}
