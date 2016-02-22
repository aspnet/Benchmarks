// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Sockets;
using System.Threading;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public sealed class RIOTcpServer
    {
        IntPtr _socket;
        RIO _rio;
        RIOThreadPool _pool;

        long _connectionId;

        public RIOTcpServer(ushort port, byte address1, byte address2, byte address3, byte address4)
        {
            var version = new Version(2, 2);
            WSAData data;
            SocketError result = RIOImports.WSAStartup((short)version.Raw, out data);
            if (result != SocketError.Success)
            {
                var error = RIOImports.WSAGetLastError();
                throw new Exception(string.Format("ERROR: WSAStartup returned {0}", error));
            }

            _socket = RIOImports.WSASocket(ADDRESS_FAMILIES.AF_INET, SOCKET_TYPE.SOCK_STREAM, PROTOCOL.IPPROTO_TCP, IntPtr.Zero, 0, SOCKET_FLAGS.REGISTERED_IO);
            if (_socket == IntPtr.Zero)
            {
                var error = RIOImports.WSAGetLastError();
                RIOImports.WSACleanup();
                throw new Exception(string.Format("ERROR: WSASocket returned {0}", error));
            }

            _rio = RIOImports.Initalize(_socket);


            _pool = new RIOThreadPool(_rio, _socket, CancellationToken.None);
            _connectionId = 0;
            Start(port, address1, address2, address3, address4);
        }

        private void Start(ushort port, byte address1, byte address2, byte address3, byte address4)
        {
            // BIND
            in_addr inAddress = new in_addr();
            inAddress.s_b1 = address1;
            inAddress.s_b2 = address2;
            inAddress.s_b3 = address3;
            inAddress.s_b4 = address4;

            sockaddr_in sa = new sockaddr_in();
            sa.sin_family = ADDRESS_FAMILIES.AF_INET;
            sa.sin_port = RIOImports.htons(port);
            sa.sin_addr = inAddress;

            int result;
            unsafe
            {
                var size = sizeof(sockaddr_in);
                result = RIOImports.bind(_socket, ref sa, size);
            }
            if (result == RIOImports.SOCKET_ERROR)
            {
                RIOImports.WSACleanup();
                throw new Exception("bind failed");
            }

            // LISTEN
            result = RIOImports.listen(_socket, 2048);
            if (result == RIOImports.SOCKET_ERROR)
            {
                RIOImports.WSACleanup();
                throw new Exception("listen failed");
            }
        }
        public RIOTcpConnection Accept()
        {
            IntPtr accepted = RIOImports.accept(_socket, IntPtr.Zero, 0);
            if (accepted == new IntPtr(-1))
            {
                var error = RIOImports.WSAGetLastError();
                RIOImports.WSACleanup();
                throw new Exception(string.Format("listen failed with {0}", error));
            }
            var connection = Interlocked.Increment(ref _connectionId);
            return new RIOTcpConnection(accepted, connection, _pool.GetWorker(connection), _rio);
        }

        public void Stop()
        {
            RIOImports.WSACleanup();
        }

        public struct Version
        {
            public ushort Raw;

            public Version(byte major, byte minor)
            {
                Raw = major;
                Raw <<= 8;
                Raw += minor;
            }

            public byte Major
            {
                get
                {
                    ushort result = Raw;
                    result >>= 8;
                    return (byte)result;
                }
            }

            public byte Minor
            {
                get
                {
                    ushort result = Raw;
                    result &= 0x00FF;
                    return (byte)result;
                }
            }

            public override string ToString()
            {
                return string.Format("{0}.{1}", Major, Minor);
            }
        }
    }
    
}
