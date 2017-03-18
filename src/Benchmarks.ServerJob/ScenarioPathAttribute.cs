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
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            if (paths.Length == 0)
            {
                throw new ArgumentException("Do not use this attribute without at least one path.", nameof(paths));
            }

            Paths = paths;
        }

        public string[] Paths { get; }
    }
}
