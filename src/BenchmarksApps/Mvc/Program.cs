using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mvc
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine($"MVC {string.Join(" ", args)}");
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>()
                        .ConfigureKestrel(options =>
                        {
                            options.ConfigureHttpsDefaults(opt =>
                            {
                                opt.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                opt.AllowAnyClientCertificate();
                            });
                        });
                });
    }
}
