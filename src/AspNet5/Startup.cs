using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;

namespace AspNet5
{
    public class Startup
    {
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("HelloWorld");

        public void Configure(IApplicationBuilder app)
        {
            app.Run(context =>
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength = _helloWorldPayload.Length;
                return context.Response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldPayload.Length);
                
            });
        }
    }
}
