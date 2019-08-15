using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Template
{
    public class Startup
    {
        public Startup(IConfiguration configuration) => Configuration = configuration;

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseRouting();

            app.UseEndpoints(routeBuilder =>
            {
                routeBuilder.Map("InvariantCultureIgnoreCase/{count}", context =>
                {
                    int count = int.Parse((string)context.GetRouteValue("count"));

                    var data = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
                    for (var i = 0; i < count; i++)
                    {
                        data.TryAdd("Id_", i);
                    }

                    Consume(data);

                    return Task.CompletedTask;
                });
            });
        }

        // avoid possible dead code elimination
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Consume<T>(in T _) { }
    }
}
