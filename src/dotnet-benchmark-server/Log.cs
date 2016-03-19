using System;

namespace BenchmarkServer
{
    public static class Log
    {
        public static void WriteLine(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
