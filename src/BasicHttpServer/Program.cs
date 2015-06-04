using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BasicHttpServer
{
    public class Program
    {
        static readonly string responseStr = "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain;charset=UTF-8\r\n" +
            "Content-Length: 10\r\n" +
            "Connection: keep-alive\r\n" +
            "Server: Dummy\r\n" +
            "\r\n" +
            "HelloWorld";


        private static byte[] _responseBytes = Encoding.UTF8.GetBytes(responseStr);

        static void Main(string[] args)
        {
            var ss = new Socket(SocketType.Stream, ProtocolType.Tcp);
            ss.Bind(new IPEndPoint(IPAddress.Loopback, 1001));
            ss.Listen(50);

            ThreadPool.SetMinThreads(100, 100);

            while (true)
            {
                var socket = ss.Accept();
                ThreadPool.QueueUserWorkItem(_ => Serve(socket));
            }
        }

        static void Serve(Socket socket)
        {
            socket.NoDelay = true;

            try
            {
                var x = 0;
                var buffer = new byte[2048];

                while (true)
                {
                    var stream = new NetworkStream(socket);
                    int r = stream.Read(buffer, 0, buffer.Length);

                    if (r == 0)
                    {
                        Console.WriteLine("quitting");
                        break;
                    }

                    for (int i = 0; i < r; i++)
                    {
                        x += buffer[i];
                    }

                    stream.Write(_responseBytes, 0, _responseBytes.Length);
                    stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                socket.Close();
            }
        }
    }
}
