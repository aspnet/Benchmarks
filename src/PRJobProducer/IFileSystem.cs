// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;

namespace PRJobProducer
{
    public interface IFileSystem
    {
        Task CreateDirectoryIfNotExists(string destination);
        Task<bool> FileExists(string location);
        Task<Stream> ReadFile(string source);
        Task WriteFile(Stream fileStream, string destination);
    }
}
