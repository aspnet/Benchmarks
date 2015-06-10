using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;

namespace AspNet5
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app)
        {
            app.Run( (context) =>
            {
                context.Response.ContentLength = 10;
                return context.Response.WriteAsync("HelloWorld");
            });
        }
    }
}
