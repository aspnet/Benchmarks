// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace BenchmarkServer
{
    public class ProcessResult
    {
        public ProcessResult(string standardOutput, string standardError, int exitCode)
        {
            StandardOutput = standardOutput;
            StandardError = standardError;
            ExitCode = exitCode;
        }

        public string StandardOutput { get; }
        public string StandardError { get; }
        public int ExitCode { get; }
    }
}
