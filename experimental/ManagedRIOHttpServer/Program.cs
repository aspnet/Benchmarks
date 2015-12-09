// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Numerics;
using System.Threading;

namespace ManagedRIOHttpServer
{
    public sealed class Program
    {
        static void Main(string[] args)
        {
            if (IntPtr.Size != 8)
            {
                Console.WriteLine("ManagedRIOHttpServer needs to be run in x64 mode");
                return;
            }

            ThreadPool.SetMinThreads(100, 100);

            Console.WriteLine("Starting Managed Registered IO Server");
            Console.WriteLine("* Hardware Accelerated SIMD: {0}", Vector.IsHardwareAccelerated);
            Console.WriteLine("* Vector<byte>.Count: {0}", Vector<byte>.Count);
            
            try
            {
                var server = new RIOServer(5000);
                server.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Start up issue {0}", ex.Message);
            }
        }

        //static byte[] careBytes = new byte[] {
        //    0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
        //    0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
        //    0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
        //    0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
        //    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        //    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        //    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        //    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
        //};

        //public static void LowerCaseSIMD(ArraySegment<byte> data)
        //{
        //    if (data.Offset + data.Count + Vector<byte>.Count < data.Array.Length)
        //    {
        //        throw new ArgumentOutOfRangeException("Nope");
        //    }
        //    var A = new Vector<byte>(65); // A
        //    var Z = new Vector<byte>(90); // Z

        //    for (var o = data.Offset; o < data.Count - Vector<byte>.Count; o += Vector<byte>.Count)
        //    {
        //        var v = new Vector<byte>(data.Array, o);

        //        v = Vector.ConditionalSelect(
        //            Vector.BitwiseAnd(
        //                Vector.GreaterThanOrEqual(v, A),
        //                Vector.LessThanOrEqual(v, Z)
        //            ),
        //            Vector.BitwiseOr(new Vector<byte>(0x20), v), // 0010 0000
        //            v
        //        );
        //        v.CopyTo(data.Array, o);
        //    }
        //}
    }

}

