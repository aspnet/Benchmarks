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
                string SayHello(string name, int age, string location) => $"Hello, {name}! You're {age} years old and based in {location}.";
                // With no filters
                endpoints.MapGet("/hello/{name}/{age}/{location}", SayHello);
                // With a filter that no-ops
                endpoints.MapGet("/helloEmptyFilter/{name}/{age}/{location}", SayHello)
                    .AddFilter((context, next) => next(context));
                // With filter on endpoint with no parameters
                endpoints.MapGet("/plaintextNoParamsWithFilter", Plaintext)
                    .AddFilter((context, next) => next(context));
                endpoints
                    .MapGet("/helloFiltered/{name}/{age}/{location}", SayHello)
                    .AddFilter((routeHandlerContext, next) => {
                        var hasParams = routeHandlerContext.MethodInfo.GetParameters().Length >= 1;
                        var isStringParam = routeHandlerContext.MethodInfo.GetParameters()[0].ParameterType == typeof(string);

                        return (RouteHandlerFilterDelegate)((context) =>
                        {
                            if (hasParams && isStringParam)
                            {
                                context.Parameters[0] = ((string)context.Parameters[0]).ToUpperInvariant();
                            }
                            return next(context);
                        });
                    });
            });

        });
    })
    .Build();

await host.StartAsync();

Console.WriteLine("Application started.");

await host.WaitForShutdownAsync();

record Todo(int Id, string Name, bool IsComplete);
