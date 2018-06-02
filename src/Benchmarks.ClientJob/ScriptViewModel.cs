// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;

namespace Benchmarks.ClientJob
{
    public class ScriptViewModel
    {
        public int Id { get; set; }
        public string SourceFileName { get; set; }
        public IFormFile Content { get; set; }
    }
}
