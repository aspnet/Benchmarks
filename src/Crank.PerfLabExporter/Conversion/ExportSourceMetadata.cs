// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Crank.PerfLabExporter.Conversion
{
    public sealed record ExportSourceMetadata(
        string CrankResultPath,
        string CounterPolicyPath,
        string ExportIdentitySource);
}
