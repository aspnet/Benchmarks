using BenchmarkDotNet.Running;

namespace Benchmarks.LabPerf
{
    class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<Md5VsSha256>(new DefaultCorePerfLabConfig());
        }
    }
}
