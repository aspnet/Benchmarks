using System.Text;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;

namespace AspNet5
{
    public class Startup
    {
        byte[] HelloWorldBuffer = Encoding.UTF8.GetBytes("Hello, World!");

        public void Configure(IApplicationBuilder app)
        {
            app.Run((context) =>
            {
                context.Response.Headers.Append("Content-Type", "text/plain");
                context.Response.ContentLength = 13;
                return context.Response.Body.WriteAsync(HelloWorldBuffer, 0, 13);
            });
        }
    }
}
