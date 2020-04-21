using System;
using System.Collections.Generic;
using System.Text;

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
