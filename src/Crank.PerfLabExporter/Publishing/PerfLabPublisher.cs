// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Crank.PerfLabExporter.Contracts;

namespace Crank.PerfLabExporter.Publishing
{
    public sealed record PublicationRetryOptions(
        int MaximumAttempts,
        TimeSpan InitialDelay)
    {
        public static PublicationRetryOptions Default { get; } = new(3, TimeSpan.FromSeconds(2));
    }

    public interface IRetryDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class TaskRetryDelay : IRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }

    public sealed class PerfLabPublicationException : Exception
    {
        public PerfLabPublicationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public interface IPerfLabPublisher
    {
        Task<string> PublishAsync(
            string container,
            string queue,
            string blobName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default);
    }

    public sealed class PerfLabPublisher : IPerfLabPublisher
    {
        private readonly IPerfLabStorageClient _storageClient;
        private readonly PublicationRetryOptions _retryOptions;
        private readonly IRetryDelay _retryDelay;
        private readonly Action<string>? _log;

        public PerfLabPublisher(
            IPerfLabStorageClient storageClient,
            PublicationRetryOptions retryOptions,
            IRetryDelay retryDelay,
            Action<string>? log = null)
        {
            if (retryOptions.MaximumAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retryOptions),
                    "At least one publication attempt is required.");
            }

            if (retryOptions.InitialDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retryOptions),
                    "The retry delay cannot be negative.");
            }

            _storageClient = storageClient;
            _retryOptions = retryOptions;
            _retryDelay = retryDelay;
            _log = log;
        }

        public async Task<string> PublishAsync(
            string container,
            string queue,
            string blobName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(container);
            ArgumentException.ThrowIfNullOrWhiteSpace(queue);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

            await ExecuteWithRetryAsync(
                "blob upload",
                token => _storageClient.UploadBlobAsync(container, blobName, content, token),
                cancellationToken);
            var message = CreateQueueMessage(container, blobName);
            await ExecuteWithRetryAsync(
                "queue submission",
                token => _storageClient.SendQueueMessageAsync(queue, message, token),
                cancellationToken);
            return message;
        }

        public static string CreateQueueMessage(string container, string blobName)
        {
            var serializerOptions = ContractJson.CreateSerializerOptions();
            return
                $"{{\"container_name\": {JsonSerializer.Serialize(container, serializerOptions)}, " +
                $"\"blob_name\": {JsonSerializer.Serialize(blobName, serializerOptions)}}}";
        }

        private async Task ExecuteWithRetryAsync(
            string operation,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken)
        {
            var delay = _retryOptions.InitialDelay;
            for (var attempt = 1; attempt <= _retryOptions.MaximumAttempts; attempt++)
            {
                try
                {
                    await action(cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception) when (attempt < _retryOptions.MaximumAttempts)
                {
                    _log?.Invoke($"{operation} attempt {attempt} failed; retrying.");
                    await _retryDelay.DelayAsync(delay, cancellationToken);
                    delay += delay;
                }
                catch (Exception exception)
                {
                    throw new PerfLabPublicationException(
                        $"{operation} failed after {attempt} attempt(s): {exception.Message}",
                        exception);
                }
            }
        }
    }
}
