using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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

        public static IHostBuilder CreateHostBuilder(string[] args)
            => Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>()
                        .ConfigureKestrel(options =>
                        {
#if CERTAUTH
                            options.ConfigureHttpsDefaults(opt =>
                            {
                                opt.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                opt.AllowAnyClientCertificate();
                                // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Benchmark code, not a secret")]
                                opt.ServerCertificate = new X509Certificate2("testCert.pfx", "testPassword");
                            });
#endif
                        });
                });
    }
}
