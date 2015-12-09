// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace ManagedRIOHttpServer.RegisteredIO
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RIO_NOTIFICATION_COMPLETION_EVENT
    {
        public IntPtr EventHandle;
        public bool NotifyReset;
    }
}
