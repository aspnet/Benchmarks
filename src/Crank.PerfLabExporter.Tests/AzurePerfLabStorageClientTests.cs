// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Crank.PerfLabExporter.Publishing;

namespace Crank.PerfLabExporter.Tests
{
    public class AzurePerfLabStorageClientTests
    {
        [Fact]
        public void ConnectionStringPreservesAzuriteServicePortsAndAccountPaths()
        {
            var storage = new AzurePerfLabStorageClient(
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                "AccountKey=YWJjZA==;" +
                "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;" +
                "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;" +
                "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;");
            var blobs = GetClient<BlobServiceClient>(storage, "_blobServiceClient");
            var queues = GetClient<QueueServiceClient>(storage, "_queueServiceClient");

            Assert.Equal("http://127.0.0.1:10000/devstoreaccount1", blobs.Uri.AbsoluteUri);
            Assert.Equal("http://127.0.0.1:10001/devstoreaccount1", queues.Uri.AbsoluteUri);
            Assert.Equal(
                "http://127.0.0.1:10000/devstoreaccount1/results/report.json",
                blobs.GetBlobContainerClient("results").GetBlobClient("report.json").Uri.AbsoluteUri);
            var queue = queues.GetQueueClient("resultsqueue");
            Assert.Equal(
                "http://127.0.0.1:10001/devstoreaccount1/resultsqueue",
                queue.Uri.AbsoluteUri);
            var configuration = typeof(QueueClient)
                .GetField("_clientConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(queue)!;
            Assert.Equal(
                QueueMessageEncoding.Base64,
                configuration.GetType().GetProperty("MessageEncoding")!.GetValue(configuration));
        }

        private static T GetClient<T>(AzurePerfLabStorageClient storage, string field)
        {
            return Assert.IsType<T>(typeof(AzurePerfLabStorageClient)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(storage));
        }
    }
}
