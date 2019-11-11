// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace PRJobProducer
{
    class LocalFileSystem : IFileSystem
    {
        private readonly string _basePath;

        public LocalFileSystem(string basePath)
        {
            _basePath = basePath;

            if (!Directory.Exists(basePath))
            {
                throw new ArgumentException($"No directory exists at '{basePath}'.");
            }
        }

        public Task CreateDirectoryIfNotExists(string destination)
        {
            // CreateDirectory no-ops if the subdirectory already exists.
            Directory.CreateDirectory(Path.Combine(_basePath, destination));
            return Task.CompletedTask;
        }

        public Task<bool> FileExists(string location)
        {
            return Task.FromResult(File.Exists(Path.Combine(_basePath, location)));
        }

        public Task<Stream> ReadFile(string source)
        {
            return Task.FromResult<Stream>(File.OpenRead(Path.Combine(_basePath, source)));
        }

        public async Task WriteFile(Stream fileStream, string destination)
        {
            // Write to a temp file first, so the JobConsumer doesn't see a partially written file.
            var tmpFilePath = Path.GetTempFileName();
            var tmpFile = new FileInfo(tmpFilePath);

            try
            {
                using (var tmpFileStream = File.OpenWrite(tmpFilePath))
                {
                    await fileStream.CopyToAsync(tmpFileStream);
                }

                // Moving a file is atomic.
                tmpFile.MoveTo(Path.Combine(_basePath, destination));
            }
            catch
            {
                tmpFile.Delete();
                throw;
            }
        }
    }
}
