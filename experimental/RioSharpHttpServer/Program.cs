using RioSharp;
using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RioSharpHttpServer
{
    class Program
    {
        static RIO_BUFSEGMENT currentSegment;
        static RioFixedBufferPool sendPool, recivePool;
        private static RioTcpListener listener;
        private static uint pipeLineDeph;
        private static byte[] responseBytes;

        public static byte[] GetResponse()
        {
            var responseStr = "HTTP/1.1 200 OK\r\n" +
                              "Content-Type: text/plain\r\n" +
                              "Content-Length: 13\r\n" +
                              "Date: " + DateTime.UtcNow.ToString("r") + "\r\n" + //"Connection: keep-alive\r\n" +
                              "Server: Dummy\r\n" +
                              "\r\n" +
                              "Hello, World!";

            return Encoding.ASCII.GetBytes(responseStr);
        }

        static void UpdateResponse()
        {
            responseBytes = GetResponse();
            var newSegment = listener.PreAllocateWrite(responseBytes);
            var oldSegment = currentSegment;
            currentSegment = newSegment;
            listener.FreePreAllocated(oldSegment);
        }


        static void Main(string[] args)
        {
            pipeLineDeph = uint.Parse(args.FirstOrDefault(f => f.StartsWith("-p"))?.Substring(2) ?? "1");
            uint connections = uint.Parse(args.FirstOrDefault(f => f.StartsWith("-c"))?.Substring(2) ?? "1");

            sendPool = new RioFixedBufferPool(1000, 140 * pipeLineDeph);
            recivePool = new RioFixedBufferPool(1000, 64 * pipeLineDeph);

            listener = new RioTcpListener(sendPool, recivePool);
            currentSegment = listener.PreAllocateWrite(GetResponse());
            Task.Run(async () =>
            {
                while (true)
                {
                    UpdateResponse();
                    await Task.Delay(1000);
                }
            });

            listener.Bind(new IPEndPoint(new IPAddress(new byte[] { 0, 0, 0, 0 }), 5000));
            listener.Listen(1024 * (int)connections);
            while (true)
            {
                var socket = listener.Accept();
                Task.Run(() => Servebuff(socket));
            }
        }

        static async Task ServeFixed(RioTcpSocket socket)
        {
            try
            {
                var buffer = new byte[64 * pipeLineDeph];
                var leftoverLength = 0;
                var oldleftoverLength = 0;
                uint endOfRequest = 0x0a0d0a0d;
                uint current = 0;

                while (true)
                {
                    int r = await socket.Stream.ReadAsync(buffer, 0, buffer.Length);
                    if (r == 0)
                        break;


                    for (int i = 0; leftoverLength != 0 && i < 4 - leftoverLength; i++)
                    {
                        current += buffer[i];
                        current = current << 8;
                        if (current == endOfRequest)
                            socket.WritePreAllocated(currentSegment);
                    }

                    leftoverLength = r % 4;
                    var length = r - leftoverLength;

                    unsafe
                    {
                        fixed (byte* currentPtr = &buffer[oldleftoverLength])
                        {
                            var start = currentPtr;
                            var end = currentPtr + length;

                            for (; start <= end; start++)
                            {
                                if (*(uint*)start == endOfRequest)
                                    socket.WritePreAllocated(currentSegment);
                            }
                        }
                    }

                    oldleftoverLength = leftoverLength;

                    for (int i = r - leftoverLength; i < r; i++)
                    {
                        current += buffer[i];
                        current = current << 4;
                    }
                    socket.Stream.Flush(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                socket.Dispose();
            }
        }

        static async Task Servebuff(RioTcpSocket socket)
        {
            try
            {
                var buffer = new byte[64 * pipeLineDeph];
                var leftoverLength = 0;
                var oldleftoverLength = 0;
                uint endOfRequest = 0x0a0d0a0d;
                uint current = 0;

                while (true)
                {
                    int r = await socket.Stream.ReadAsync(buffer, 0, buffer.Length);
                    if (r == 0)
                        break;


                    for (int i = 0; leftoverLength != 0 && i < 4 - leftoverLength; i++)
                    {
                        current += buffer[i];
                        current = current << 8;
                        if (current == endOfRequest)
                            socket.Stream.Write(responseBytes, 0, responseBytes.Length);
                    }

                    leftoverLength = r % 4;
                    var length = r - leftoverLength;

                    unsafe
                    {
                        fixed (byte* currentPtr = &buffer[oldleftoverLength])
                        {
                            var start = currentPtr;
                            var end = currentPtr + length;

                            for (; start <= end; start++)
                            {
                                if (*(uint*)start == endOfRequest)
                                    socket.Stream.Write(responseBytes, 0, responseBytes.Length);
                            }
                        }
                    }

                    oldleftoverLength = leftoverLength;

                    for (int i = r - leftoverLength; i < r; i++)
                    {
                        current += buffer[i];
                        current = current << 4;
                    }
                    socket.Stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                socket.Dispose();
            }
        }
    }

}
