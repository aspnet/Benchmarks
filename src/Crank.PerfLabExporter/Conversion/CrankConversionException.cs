// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Crank.PerfLabExporter.Conversion
{
    public sealed class CrankConversionException : Exception
    {
        public CrankConversionException(string message)
            : base(message)
        {
        }

        public CrankConversionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
