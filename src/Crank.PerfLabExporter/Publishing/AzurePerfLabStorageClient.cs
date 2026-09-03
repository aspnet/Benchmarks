// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;

namespace Crank.PerfLabExporter.Publishing
{
    public sealed class AzurePerfLabStorageClient : IPerfLabStorageClient
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;

        public AzurePerfLabStorageClient(
            StorageAccountEndpoints endpoints,
            TokenCredential credential)
        {
            _blobServiceClient = new BlobServiceClient(endpoints.BlobServiceUri, credential);
            _queueServiceClient = new QueueServiceClient(
                endpoints.QueueServiceUri,
                credential,
                new QueueClientOptions
                {
                    MessageEncoding = QueueMessageEncoding.Base64
                });
        }

        public async Task UploadBlobAsync(
            string container,
            string blobName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            var blob = _blobServiceClient
                .GetBlobContainerClient(container)
                .GetBlobClient(blobName);
            await blob.UploadAsync(
                BinaryData.FromBytes(content),
                overwrite: true,
                cancellationToken);
            await blob.SetHttpHeadersAsync(
                new BlobHttpHeaders { ContentType = "application/json" },
                cancellationToken: cancellationToken);
        }

        public async Task SendQueueMessageAsync(
            string queue,
            string message,
            CancellationToken cancellationToken)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queue);
            await queueClient.SendMessageAsync(message, cancellationToken);
        }
    }
}
