// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Crank.PerfLabExporter
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;
            try
            {
                return await new ExporterApplication(Console.Out, Console.Error)
                    .RunAsync(args, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Console.Error.WriteLine("error: export cancelled.");
                return 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"error: {exception.Message}");
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
            }

            void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            }
        }
    }
}
