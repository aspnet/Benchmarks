// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NativeRIOHttpServer.RegisteredIO
{
    public enum RIO_NOTIFICATION_COMPLETION_TYPE : int
    {
        POLLING = 0,
        EVENT_COMPLETION = 1,
        IOCP_COMPLETION = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RIO_NOTIFICATION_COMPLETION_EVENT
    {
        public IntPtr EventHandle;
        public bool NotifyReset;
    }
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RIO_NOTIFICATION_COMPLETION_IOCP
    {
        public IntPtr IocpHandle;
        public ulong QueueCorrelation;
        public NativeOverlapped* Overlapped;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RIO_NOTIFICATION_COMPLETION
    {
        public RIO_NOTIFICATION_COMPLETION_TYPE Type;
        public RIO_NOTIFICATION_COMPLETION_IOCP Iocp;
    }
}
