using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace Downstream
{
    public class Program
    {
        private static readonly byte[] _response = Encoding.UTF8.GetBytes(new string('a', 1024));
        private static readonly int _responseLength = _response.Length;

        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            new WebHostBuilder()
                .UseKestrel()
                .UseConfiguration(config)
                .Configure(app => app.Run(async (context) =>
                {
                    await context.Response.Body.WriteAsync(_response, 0, _response.Length);
                }))
                .Build()
                .Run();
        }
    }
}
