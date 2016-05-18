// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;

namespace Benchmarks.ServerJob
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class ScenarioPathAttribute : Attribute
    {
        public ScenarioPathAttribute(params string[] paths)
        {
            Paths = paths;
        }

        public string[] Paths { get; }
    }
}
