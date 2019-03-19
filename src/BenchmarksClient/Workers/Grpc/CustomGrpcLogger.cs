// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Grpc.Core.Logging;

namespace BenchmarksClient.Workers.Grpc
{
    public class CustomGrpcLogger : ILogger
    {
        private readonly Action<string> _logger;

        public CustomGrpcLogger(Action<string> logger)
        {
            _logger = logger;
        }

        public void Debug(string message)
        {
            _logger("Debug - " + message);
        }

        public void Debug(string format, params object[] formatArgs)
        {
            _logger("Debug - " + string.Format(format, formatArgs));
        }

        public void Error(string message)
        {
            _logger("Error - " + message);
        }

        public void Error(string format, params object[] formatArgs)
        {
            _logger("Error - " + string.Format(format, formatArgs));
        }

        public void Error(Exception exception, string message)
        {
            _logger("Error - " + message + " " + exception?.ToString());
        }

        public ILogger ForType<T>()
        {
            return this;
        }

        public void Info(string message)
        {
            _logger("Info - " + message);
        }

        public void Info(string format, params object[] formatArgs)
        {
            _logger("Info - " + string.Format(format, formatArgs));
        }

        public void Warning(string message)
        {
            _logger("Warning - " + message);
        }

        public void Warning(string format, params object[] formatArgs)
        {
            _logger("Warning - " + string.Format(format, formatArgs));
        }

        public void Warning(Exception exception, string message)
        {
            _logger("Warning - " + message + " " + exception?.ToString());
        }
    }
}
