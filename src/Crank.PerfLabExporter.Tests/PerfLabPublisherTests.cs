// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;
using Crank.PerfLabExporter.Publishing;

namespace Crank.PerfLabExporter.Tests
{
    public class PerfLabPublisherTests
    {
        [Fact]
        public void MatchesPerformanceUploadQueuePayloadExactly()
        {
            var message = PerfLabPublisher.CreateQueueMessage(
                "results",
                "crank/family/queue/hash/report.perflab.json");

            Assert.Equal(
                "{\"container_name\": \"results\", \"blob_name\": \"crank/family/queue/hash/report.perflab.json\"}",
                message);
        }

        [Fact]
        public async Task RetriesBlobAndQueueOperationsWithStableIdentity()
        {
            var storage = new StubStorageClient
            {
                UploadFailuresRemaining = 2,
                QueueFailuresRemaining = 1
            };
            var delay = new StubRetryDelay();
            var publisher = new PerfLabPublisher(
                storage,
                new PublicationRetryOptions(3, TimeSpan.FromMilliseconds(1)),
                delay);
            var content = Encoding.UTF8.GetBytes("{\"report\":true}");

            var message = await publisher.PublishAsync(
                "results",
                "resultsqueue",
                "deterministic/report.json",
                content);

            Assert.Equal(3, storage.UploadAttempts);
            Assert.Equal(2, storage.QueueAttempts);
            Assert.Equal(3, delay.Delays.Count);
            Assert.All(storage.BlobNames, name => Assert.Equal("deterministic/report.json", name));
            Assert.All(storage.UploadedContent, bytes => Assert.Equal(content, bytes));
            Assert.Equal(message, storage.Messages.Single());
        }

        [Fact]
        public async Task SurfacesPermanentQueueFailureAfterRetries()
        {
            var storage = new StubStorageClient
            {
                QueueFailuresRemaining = int.MaxValue
            };
            var publisher = new PerfLabPublisher(
                storage,
                new PublicationRetryOptions(2, TimeSpan.Zero),
                new StubRetryDelay());

            var exception = await Assert.ThrowsAsync<PerfLabPublicationException>(() =>
                publisher.PublishAsync(
                    "results",
                    "resultsqueue",
                    "deterministic/report.json",
                    Encoding.UTF8.GetBytes("{}")));

            Assert.Contains("queue submission failed after 2 attempt", exception.Message, StringComparison.Ordinal);
            Assert.Equal(1, storage.UploadAttempts);
            Assert.Equal(2, storage.QueueAttempts);
        }

        [Fact]
        public void InvalidStorageUriDoesNotEchoQuerySecrets()
        {
            const string marker = "sensitive-marker";

            var exception = Assert.Throws<ArgumentException>(() =>
                StorageAccountEndpoints.Parse(
                    $"https://account.blob.core.windows.net/?sig={marker}"));

            Assert.DoesNotContain(marker, exception.Message, StringComparison.Ordinal);
        }

        private sealed class StubStorageClient : IPerfLabStorageClient
        {
            public int UploadFailuresRemaining { get; set; }

            public int QueueFailuresRemaining { get; set; }

            public int UploadAttempts { get; private set; }

            public int QueueAttempts { get; private set; }

            public List<string> BlobNames { get; } = [];

            public List<byte[]> UploadedContent { get; } = [];

            public List<string> Messages { get; } = [];

            public Task UploadBlobAsync(
                string container,
                string blobName,
                ReadOnlyMemory<byte> content,
                CancellationToken cancellationToken)
            {
                UploadAttempts++;
                BlobNames.Add(blobName);
                UploadedContent.Add(content.ToArray());
                if (UploadFailuresRemaining-- > 0)
                {
                    throw new TimeoutException("transient upload failure");
                }

                return Task.CompletedTask;
            }

            public Task SendQueueMessageAsync(
                string queue,
                string message,
                CancellationToken cancellationToken)
            {
                QueueAttempts++;
                if (QueueFailuresRemaining-- > 0)
                {
                    throw new TimeoutException("queue failure");
                }

                Messages.Add(message);
                return Task.CompletedTask;
            }
        }

        private sealed class StubRetryDelay : IRetryDelay
        {
            public List<TimeSpan> Delays { get; } = [];

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            }
        }
    }
}
