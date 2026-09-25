// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Crank.PerfLabExporter.Publishing
{
    public interface IPerfLabStorageClient
    {
        Task UploadBlobAsync(
            string container,
            string blobName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken);

        Task SendQueueMessageAsync(
            string queue,
            string message,
            CancellationToken cancellationToken);
    }
}
