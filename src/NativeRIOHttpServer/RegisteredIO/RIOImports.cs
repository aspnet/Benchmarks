// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;

namespace NativeRIOHttpServer.RegisteredIO
{
    public class RIO
    {
        public RIOImports.RIORegisterBuffer RegisterBuffer;

        public RIOImports.RIOCreateCompletionQueue CreateCompletionQueue;
        public RIOImports.RIOCreateRequestQueue CreateRequestQueue;


        public RIOImports.RIOReceive Receive;
        public RIOImports.RIOSend Send;

        public RIOImports.RIONotify Notify;

        public RIOImports.RIOCloseCompletionQueue CloseCompletionQueue;
        public RIOImports.RIODequeueCompletion DequeueCompletion;
        public RIOImports.RIODeregisterBuffer DeregisterBuffer;
        public RIOImports.RIOResizeCompletionQueue ResizeCompletionQueue;
        public RIOImports.RIOResizeRequestQueue ResizeRequestQueue;


        public const long CachedValue = long.MinValue;

        public RIO()
        {
        }
    }

    public static class RIOImports
    {
        const string WS2_32 = "WS2_32.dll";

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct RIO_EXTENSION_FUNCTION_TABLE
        {
            public UInt32 cbSize;

            public IntPtr RIOReceive;
            public IntPtr RIOReceiveEx;
            public IntPtr RIOSend;
            public IntPtr RIOSendEx;
            public IntPtr RIOCloseCompletionQueue;
            public IntPtr RIOCreateCompletionQueue;
            public IntPtr RIOCreateRequestQueue;
            public IntPtr RIODequeueCompletion;
            public IntPtr RIODeregisterBuffer;
            public IntPtr RIONotify;
            public IntPtr RIORegisterBuffer;
            public IntPtr RIOResizeCompletionQueue;
            public IntPtr RIOResizeRequestQueue;
        }

        readonly static IntPtr RIO_INVALID_BUFFERID = (IntPtr)0xFFFFFFFF;

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate IntPtr RIORegisterBuffer([In] IntPtr DataBuffer, [In] UInt32 DataLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate void RIODeregisterBuffer([In] IntPtr BufferId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public unsafe delegate bool RIOSend([In] IntPtr SocketQueue, [In] RIO_BUFSEGMENT* RioBuffer, [In] UInt32 DataBufferCount, [In] RIO_SEND_FLAGS Flags, [In] long RequestCorrelation);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate bool RIOReceive([In] IntPtr SocketQueue, [In] ref RIO_BUFSEGMENT RioBuffer, [In] UInt32 DataBufferCount, [In] RIO_RECEIVE_FLAGS Flags, [In] long RequestCorrelation);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate IntPtr RIOCreateCompletionQueue([In] uint QueueSize, [In] RIO_NOTIFICATION_COMPLETION NotificationCompletion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate void RIOCloseCompletionQueue([In] IntPtr CQ);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate IntPtr RIOCreateRequestQueue(
                                      [In] IntPtr Socket,
                                      [In] UInt32 MaxOutstandingReceive,
                                      [In] UInt32 MaxReceiveDataBuffers,
                                      [In] UInt32 MaxOutstandingSend,
                                      [In] UInt32 MaxSendDataBuffers,
                                      [In] IntPtr ReceiveCQ,
                                      [In] IntPtr SendCQ,
                                      [In] long ConnectionCorrelation
                                    );

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate uint RIODequeueCompletion([In] IntPtr CQ, [In] IntPtr ResultArray, [In] uint ResultArrayLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate Int32 RIONotify([In] IntPtr CQ);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate bool RIOResizeCompletionQueue([In] IntPtr CQ, [In] UInt32 QueueSize);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public delegate bool RIOResizeRequestQueue([In] IntPtr RQ, [In] UInt32 MaxOutstandingReceive, [In] UInt32 MaxOutstandingSend);

        const uint IOC_OUT = 0x40000000;
        const uint IOC_IN = 0x80000000;
        const uint IOC_INOUT = IOC_IN | IOC_OUT;
        const uint IOC_WS2 = 0x08000000;
        const uint IOC_VENDOR = 0x18000000;
        const uint SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER = IOC_INOUT | IOC_WS2 | 36;
          
        const int SIO_LOOPBACK_FAST_PATH =  -1744830448;// IOC_IN | IOC_WS2 | 16;
    
        const int TCP_NODELAY = 0x0001;
        const int IPPROTO_TCP = 6;

        public unsafe static RIO Initalize(IntPtr socket)
        {

            UInt32 dwBytes = 0;
            RIO_EXTENSION_FUNCTION_TABLE rio = new RIO_EXTENSION_FUNCTION_TABLE();
            Guid RioFunctionsTableId = new Guid("8509e081-96dd-4005-b165-9e2ee8c79e3f");


            int True = -1;

            int result = setsockopt(socket, IPPROTO_TCP, TCP_NODELAY, (char*)&True, 4);
            if (result != 0)
            {
                var error = RIOImports.WSAGetLastError();
                RIOImports.WSACleanup();
                throw new Exception(String.Format("ERROR: setsockopt TCP_NODELAY returned {0}", error));
            }

            result = WSAIoctlGeneral(socket, SIO_LOOPBACK_FAST_PATH, 
                                &True, 4, null, 0,
                                out dwBytes, IntPtr.Zero, IntPtr.Zero);

            if (result != 0)
            {
                var error = RIOImports.WSAGetLastError();
                RIOImports.WSACleanup();
                throw new Exception(String.Format("ERROR: WSAIoctl SIO_LOOPBACK_FAST_PATH returned {0}", error));
            }

            result = WSAIoctl(socket, SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER,
               ref RioFunctionsTableId, 16, ref rio,
               sizeof(RIO_EXTENSION_FUNCTION_TABLE),
               out dwBytes, IntPtr.Zero, IntPtr.Zero);
            
            if (result != 0)
            {
                var error = RIOImports.WSAGetLastError();
                RIOImports.WSACleanup();
                throw new Exception(String.Format("ERROR: RIOInitalize returned {0}", error));
            }
            else
            {
                RIO rioFunctions = new RIO();

                rioFunctions.RegisterBuffer = Marshal.GetDelegateForFunctionPointer<RIORegisterBuffer>(rio.RIORegisterBuffer);

                rioFunctions.CreateCompletionQueue = Marshal.GetDelegateForFunctionPointer<RIOCreateCompletionQueue>(rio.RIOCreateCompletionQueue);

                rioFunctions.CreateRequestQueue = Marshal.GetDelegateForFunctionPointer<RIOCreateRequestQueue>(rio.RIOCreateRequestQueue);
                
                rioFunctions.Notify = Marshal.GetDelegateForFunctionPointer<RIONotify>(rio.RIONotify);
                rioFunctions.DequeueCompletion = Marshal.GetDelegateForFunctionPointer<RIODequeueCompletion>(rio.RIODequeueCompletion);

                rioFunctions.Receive = Marshal.GetDelegateForFunctionPointer<RIOReceive>(rio.RIOReceive);
                rioFunctions.Send = Marshal.GetDelegateForFunctionPointer<RIOSend>(rio.RIOSend);

                rioFunctions.CloseCompletionQueue = Marshal.GetDelegateForFunctionPointer<RIOCloseCompletionQueue>(rio.RIOCloseCompletionQueue);
                rioFunctions.DeregisterBuffer = Marshal.GetDelegateForFunctionPointer<RIODeregisterBuffer>(rio.RIODeregisterBuffer);
                rioFunctions.ResizeCompletionQueue = Marshal.GetDelegateForFunctionPointer<RIOResizeCompletionQueue>(rio.RIOResizeCompletionQueue);
                rioFunctions.ResizeRequestQueue = Marshal.GetDelegateForFunctionPointer<RIOResizeRequestQueue>(rio.RIOResizeRequestQueue);

                return rioFunctions;
            }
        }
        
        [DllImport(WS2_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern int WSAIoctl(
          [In] IntPtr socket,
          [In] uint dwIoControlCode,
          [In] ref Guid lpvInBuffer,
          [In] uint cbInBuffer,
          [In, Out] ref RIO_EXTENSION_FUNCTION_TABLE lpvOutBuffer,
          [In] int cbOutBuffer,
          [Out] out uint lpcbBytesReturned,
          [In] IntPtr lpOverlapped,
          [In] IntPtr lpCompletionRoutine
        );

        [DllImport(WS2_32, SetLastError = true, EntryPoint = "WSAIoctl"), SuppressUnmanagedCodeSecurity]
        private unsafe static extern int WSAIoctlGeneral(
          [In] IntPtr socket,
          [In] int dwIoControlCode,
          [In] int* lpvInBuffer,
          [In] uint cbInBuffer,
          [In] int* lpvOutBuffer,
          [In] int cbOutBuffer,
          [Out] out uint lpcbBytesReturned,
          [In] IntPtr lpOverlapped,
          [In] IntPtr lpCompletionRoutine
        );

        [DllImport(WS2_32, SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = true, ThrowOnUnmappableChar = true), SuppressUnmanagedCodeSecurity]
        internal static extern SocketError WSAStartup([In] short wVersionRequested, [Out] out WSAData lpWSAData );

        [DllImport(WS2_32, SetLastError = true, CharSet = CharSet.Ansi), SuppressUnmanagedCodeSecurity]
        public static extern IntPtr WSASocket([In] ADDRESS_FAMILIES af, [In] SOCKET_TYPE type, [In] PROTOCOL protocol, [In] IntPtr lpProtocolInfo, [In] Int32 group, [In] SOCKET_FLAGS dwFlags );

        [DllImport(WS2_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern ushort htons([In] ushort hostshort);

        [DllImport(WS2_32, SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int bind(IntPtr s, ref sockaddr_in name, int namelen);

        [DllImport(WS2_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern int listen(IntPtr s, int backlog);

        [DllImport(WS2_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public unsafe static extern int setsockopt(IntPtr s, int level, int optname, char* optval, int optlen);

        [DllImport(WS2_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern IntPtr accept(IntPtr s, IntPtr addr, int addrlen);
        
        [DllImport(WS2_32), SuppressUnmanagedCodeSecurity]
        public static extern Int32 WSAGetLastError();

        [DllImport(WS2_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern Int32 WSACleanup();

        [DllImport(WS2_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern int closesocket(IntPtr s);

        public const int SOCKET_ERROR = -1;
        public const int INVALID_SOCKET = -1;
    }

    public enum ADDRESS_FAMILIES : short
    {
        AF_INET = 2,
    }

    public enum SOCKET_TYPE : short
    {
        SOCK_STREAM = 1,
    }

    public enum PROTOCOL : short
    {
        IPPROTO_TCP = 6,
    }

    public enum SOCKET_FLAGS : UInt32
    {
        OVERLAPPED = 0x01,
        MULTIPOINT_C_ROOT = 0x02,
        MULTIPOINT_C_LEAF = 0x04,
        MULTIPOINT_D_ROOT = 0x08,
        MULTIPOINT_D_LEAF = 0x10,
        ACCESS_SYSTEM_SECURITY = 0x40,
        NO_HANDLE_INHERIT = 0x80,
        REGISTERED_IO = 0x100
    }

    public enum RIO_SEND_FLAGS : UInt32
    {
        NONE = 0x00000000,
        DONT_NOTIFY = 0x00000001,
        DEFER = 0x00000002,
        COMMIT_ONLY = 0x00000008
    }
    public enum RIO_RECEIVE_FLAGS : UInt32
    {
        NONE = 0x00000000,
        DONT_NOTIFY = 0x00000001,
        DEFER = 0x00000002,
        WAITALL = 0x00000004,
        COMMIT_ONLY = 0x00000008
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WSAData
    {
        internal short wVersion;
        internal short wHighVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        internal string szDescription;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
        internal string szSystemStatus;
        internal short iMaxSockets;
        internal short iMaxUdpDg;
        internal IntPtr lpVendorInfo;
    }


    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct sockaddr_in
    {
        public ADDRESS_FAMILIES sin_family;
        public ushort sin_port;
        public in_addr sin_addr;
        public fixed byte sin_zero[8];
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct in_addr
    {
        [FieldOffset(0)]
        public byte s_b1;
        [FieldOffset(1)]
        public byte s_b2;
        [FieldOffset(2)]
        public byte s_b3;
        [FieldOffset(3)]
        public byte s_b4;
    }
}
