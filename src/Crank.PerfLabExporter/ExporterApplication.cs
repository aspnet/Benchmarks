// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Crank.PerfLabExporter.CommandLine;
using Crank.PerfLabExporter.Contracts;
using Crank.PerfLabExporter.Contracts.Crank;
using Crank.PerfLabExporter.Contracts.PerfLab;
using Crank.PerfLabExporter.Contracts.Policy;
using Crank.PerfLabExporter.Conversion;
using Crank.PerfLabExporter.IO;
using Crank.PerfLabExporter.Naming;
using Crank.PerfLabExporter.Publishing;

namespace Crank.PerfLabExporter
{
    internal sealed class ExporterApplication
    {
        private readonly TextWriter _output;
        private readonly TextWriter _error;

        public ExporterApplication(TextWriter output, TextWriter error)
        {
            _output = output;
            _error = error;
        }

        public async Task<int> RunAsync(
            string[] args,
            CancellationToken cancellationToken = default)
        {
            var options = ExporterCommandLine.Parse(args);
            if (options.ShowHelp)
            {
                await _output.WriteLineAsync(ExporterCommandLine.Help);
                return 0;
            }

            var crankPath = ResolveInputPath(options.CrankJsonPath);
            var policyPath = ResolveInputPath(options.CounterPolicyPath);
            var serializerOptions = ContractJson.CreateSerializerOptions();
            var execution = await ReadJsonAsync<CrankExecutionResult>(
                crankPath,
                serializerOptions,
                cancellationToken);
            var policy = await ReadJsonAsync<CounterPolicy>(
                policyPath,
                serializerOptions,
                cancellationToken);
            var identity = LiveExportIdentityBuilder.Build(
                execution,
                options.LiveIdentity);
            var identitySource =
                $"crank-properties:{options.LiveIdentity.PropertyPrefix}";

            using var httpClient = new HttpClient();
            var githubToken = string.IsNullOrWhiteSpace(options.GitHubTokenEnvironmentVariable)
                ? null
                : Environment.GetEnvironmentVariable(options.GitHubTokenEnvironmentVariable);
            var converter = new CrankPerfLabConverter(
                new CachingCommitTimeResolver(
                    new GitHubCommitTimeResolver(httpClient, githubToken)));
            var conversion = await converter.ConvertAsync(
                execution,
                policy,
                identity,
                new ExportSourceMetadata(crankPath, policyPath, identitySource),
                cancellationToken);
            var names = ExportNaming.Create(conversion.Report, identity);
            var reportBytes = JsonSerializer.SerializeToUtf8Bytes(
                conversion.Report,
                ContractJson.CreateSerializerOptions(writeIndented: true));
            var outputPath = Path.Combine(Path.GetFullPath(options.OutputDirectory), names.FileName);
            await AtomicFileWriter.WriteAsync(outputPath, reportBytes, cancellationToken);

            foreach (var diagnostic in conversion.Diagnostics)
            {
                await _error.WriteLineAsync($"warning: {diagnostic}");
            }

            await _output.WriteLineAsync($"PerfLab JSON: {outputPath}");
            await _output.WriteLineAsync(
                "Sample model: one aggregate scalar per Crank --json result; timestamped measurements were not used as samples.");

            if (options.Mode == ExportMode.Upload)
            {
                var endpoints = StorageAccountEndpoints.Parse(options.StorageAccount!);
                var credential = AzureCredentialFactory.Create(options.Authentication);
                var storage = new AzurePerfLabStorageClient(endpoints, credential);
                var publisher = new PerfLabPublisher(
                    storage,
                    options.Retry,
                    new TaskRetryDelay(),
                    message => _error.WriteLine(message));
                await publisher.PublishAsync(
                    options.Container!,
                    options.Queue!,
                    names.BlobName,
                    reportBytes,
                    cancellationToken);
                await _output.WriteLineAsync($"Blob: {names.BlobName}");
                await _output.WriteLineAsync($"Queue: {options.Queue}");
            }

            return 0;
        }

        internal static string ResolveInputPath(string path)
        {
            if (Path.IsPathFullyQualified(path))
            {
                return Path.GetFullPath(path);
            }

            var workingDirectoryPath = Path.GetFullPath(path);
            if (File.Exists(workingDirectoryPath))
            {
                return workingDirectoryPath;
            }

            var applicationPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, path));
            return File.Exists(applicationPath)
                ? applicationPath
                : workingDirectoryPath;
        }

        private static async Task<T> ReadJsonAsync<T>(
            string path,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var value = await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    serializerOptions,
                    cancellationToken);
                return value ?? throw new JsonException($"'{path}' contains JSON null.");
            }
            catch (JsonException exception)
            {
                throw new CrankConversionException(
                    $"Could not read '{path}' as {typeof(T).Name}: {exception.Message}",
                    exception);
            }
        }
    }
}
