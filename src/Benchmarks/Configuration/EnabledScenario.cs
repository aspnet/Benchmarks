// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;

namespace Benchmarks.Configuration
{
    public class EnabledScenario(string name, IEnumerable<string> paths)
    {
        public string Name { get; } = name;

        public IEnumerable<string> Paths { get; } = paths;
    }
}
