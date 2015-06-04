using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;

namespace AspNet5
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app)
        {
            app.Run(async (context) =>
            {
                context.Response.ContentLength = 10;
                await context.Response.WriteAsync("HelloWorld");
            });
        }
    }
}
