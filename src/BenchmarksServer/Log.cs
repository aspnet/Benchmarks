// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace BenchmarkServer
{
    public static class Log
    {
        public static void WriteLine(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
