// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;

namespace Benchmarks.ClientJob
{
    public static class StringArrayExtensions
    {
        public static string ToContentString(this string[] array)
        {
            return $"[{string.Join(", ", array.Select(s => $"\"{s}\""))}]";
        }
    }
}
