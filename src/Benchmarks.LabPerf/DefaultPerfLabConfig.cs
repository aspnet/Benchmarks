using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Validators;

namespace Benchmarks.LabPerf
{
    internal class DefaultCorePerfLabConfig : ManualConfig
    {
        public DefaultCorePerfLabConfig()
        {
            Add(ConsoleLogger.Default);

            Add(MemoryDiagnoser.Default);
            Add(StatisticColumn.OperationsPerSecond);
            Add(DefaultColumnProviders.Statistics, DefaultColumnProviders.Diagnosers);
            Add(DefaultColumnProviders.Descriptor);

            Add(JitOptimizationsValidator.FailOnError);

            Add(Job.InProcess
                .With(RunStrategy.Throughput));

            Add(MarkdownExporter.GitHub);

            Add(new CsvExporter(
                CsvSeparator.Comma,
                new BenchmarkDotNet.Reports.SummaryStyle
                {
                    PrintUnitsInHeader = true,
                    PrintUnitsInContent = false,
                    TimeUnit = BenchmarkDotNet.Horology.TimeUnit.Microsecond,
                    SizeUnit = SizeUnit.KB
                }));
        }
    }
}
