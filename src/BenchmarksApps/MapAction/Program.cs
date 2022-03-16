using System;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logBuilder => logBuilder.ClearProviders())
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.Configure(app =>
        {
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                Todo EchoTodo([FromBody] Todo todo) => todo;
                endpoints.MapPost("/EchoTodo", (Func<Todo, Todo>)EchoTodo);

                string Plaintext() => "Hello, World!";
                endpoints.MapGet("/plaintext", (Func<string>)Plaintext);

                object Json() => new { message = "Hello, World!" };
                endpoints.MapGet("/json", (Func<object>)Json);

                // Parameterized plain-text endpoint
                string SayHello(string name) => $"Hello, {name}!";
                // With no filters
                endpoints.MapGet("/hello/{name}", SayHello);
                // With a filter that no-ops
                endpoints.MapGet("/helloEmptyFilter/{name}", SayHello)
                    .AddFilter((context, next) => next(context));
                endpoints
                    .MapGet("/helloFiltered/{name}", SayHello)
                    .AddFilter((routeHandlerContext, next) => {
                        var hasSingleParam = routeHandlerContext.MethodInfo.GetParameters().Length == 1;
                        var isStringParam = routeHandlerContext.MethodInfo.GetParameters()[0].ParameterType == typeof(string);

                        return (context) =>
                        {
                            if (hasSingleParam && isStringParam)
                            {
                                context.Parameters[0] = context.Parameters[0].ToUpperInvariant();
                            }
                            return next(context);
                        };
                    });
            });

        });
    })
    .Build();

await host.StartAsync();

Console.WriteLine("Application started.");

await host.WaitForShutdownAsync();

record Todo(int Id, string Name, bool IsComplete);
