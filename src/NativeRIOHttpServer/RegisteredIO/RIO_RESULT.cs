// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace NativeRIOHttpServer.RegisteredIO
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RIO_RESULT
    {
        public int Status;
        public uint BytesTransferred;
        public long ConnectionCorrelation;
        public long RequestCorrelation;
    }
}
