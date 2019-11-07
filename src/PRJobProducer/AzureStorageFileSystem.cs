// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.File;

namespace PRJobProducer
{
    public class AzureStorageFileSystem : IFileSystem
    {
        private readonly CloudFileDirectory _directory;

        public AzureStorageFileSystem(CloudFileDirectory directory)
        {
            _directory = directory;
        }

        public Task CreateDirectoryIfNotExists(string location)
        {
            var dir = GetTargetDirectory(location);
            return dir.CreateIfNotExistsAsync();
        }

        public Task<bool> FileExists(string location)
        {
            var parentDir = GetTargetDirectory(location);
            var file = parentDir.GetFileReference(Path.GetFileName(location));
            return file.ExistsAsync();
        }

        public async Task<Stream> ReadFile(string source)
        {
            var sourceDir = GetTargetDirectory(source);
            var sourceFile = sourceDir.GetFileReference(Path.GetFileName(source));
            var memoryStream = new MemoryStream();

            await sourceFile.DownloadToStreamAsync(memoryStream);

            memoryStream.Position = 0;

            return memoryStream;
        }

        public Task WriteFile(Stream fileStream, string destination)
        {
            var destinationDir = GetTargetDirectory(destination);
            var destinationFile = destinationDir.GetFileReference(Path.GetFileName(destination));

            return destinationFile.UploadFromStreamAsync(fileStream);
        }

        private CloudFileDirectory GetTargetDirectory(string targetFile)
        {
            var targetDirPath = Path.GetDirectoryName(targetFile);

            return string.IsNullOrEmpty(targetDirPath) ?
                _directory :
                _directory.GetDirectoryReference(targetDirPath);
        }
    }
}
