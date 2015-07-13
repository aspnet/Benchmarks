using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;

namespace AspNet5
{
    public class Startup
    {
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("HelloWorld");
        private static readonly Task _done = Task.FromResult(0);

        public void Configure(IApplicationBuilder app)
        {
            app.Run(context =>
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength = _helloWorldPayload.Length;
                context.Response.Body.Write(_helloWorldPayload, 0, _helloWorldPayload.Length);

                return _done;
            });
        }
    }
}
