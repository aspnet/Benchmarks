// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace AzDoConsumer
{
    public class ProcessResult
    {
        public ProcessResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output;
            Error = error;
        }

        public string Error { get; }
        public int ExitCode { get; }
        public string Output { get; }
    }
}
