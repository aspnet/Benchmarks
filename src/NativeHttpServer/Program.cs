using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NativeHttpServer
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
            // TODO: Use safehandles everywhere!
            var ss = new TcpServer(5000, 127, 0, 0, 1);

            ThreadPool.SetMinThreads(100, 100);

            while (true)
            {
                var socket = ss.Accept();
                ThreadPool.QueueUserWorkItem(_ => Serve(socket));
            }
        }

        static void Serve(TcpConnection socket)
        {
            try
            {
                var x = 0;
                var buffer = new byte[2048];

                while (true)
                {
                    int r = socket.Receive(buffer);

                    if (r == 0)
                    {
                        break;
                    }

                    for (int i = 0; i < r; i++)
                    {
                        x += buffer[i];
                    }

                    socket.Send(_responseBytes, _responseBytes.Length);
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

