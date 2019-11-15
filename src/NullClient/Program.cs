using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace NullClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Null Client");
            Console.WriteLine("Press a key to stop...");
            
            Console.WriteLine("args: " + String.Join(' ', args));
            Console.WriteLine("SERVER_URL:" + Environment.GetEnvironmentVariable("SERVER_URL"));

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey();
                    break;
                }

                await Task.Delay(1000);
            }
        }
    }
}
