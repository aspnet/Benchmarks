# MvcFull - ASP.NET MVC .NET Framework 4.8 Benchmark Project

This project is a replication of the ASP.NET Core MVC benchmark project, targeting .NET Framework 4.8 using traditional ASP.NET MVC 5.

## Key Differences from the Original Project

1. **Framework**: Targets .NET Framework 4.8 instead of .NET 8.0
2. **MVC Version**: Uses ASP.NET MVC 5 instead of ASP.NET Core MVC
3. **Entity Framework**: Uses Entity Framework 6 instead of Entity Framework Core
4. **Configuration**: Uses Web.config instead of appsettings.json
5. **Startup**: Uses Global.asax instead of Program.cs
6. **Dependency Injection**: Manual instantiation instead of built-in DI container
7. **Dapper**: Dapper implementation has been omitted as requested

## Project Structure

```
MvcFull/
??? App_Start/
?   ??? RouteConfig.cs        # Route configuration
??? Controllers/
?   ??? HomeController.cs       # Home and basic endpoints
?   ??? FortunesController.cs   # Fortunes benchmark
?   ??? SingleQueryController.cs    # Single query benchmark
?   ??? MultipleQueriesController.cs # Multiple queries benchmark
?   ??? UpdatesController.cs  # Updates benchmark
??? Database/
?   ??? ApplicationDbContext.cs # EF6 DbContext
?   ??? Db.cs     # Database operations
??? Models/
?   ??? Fortune.cs              # Fortune model
?   ??? World.cs         # World model
??? Properties/
?   ??? AssemblyInfo.cs         # Assembly metadata
??? Views/
?   ??? Fortunes/
?   ?   ??? Index.cshtml        # Fortunes view
?   ??? Home/
?   ?   ??? Index.cshtml        # Home view
?   ??? _ViewStart.cshtml       # View start (no layout)
?   ??? Web.config      # Views configuration
??? AppSettings.cs       # Application settings model
??? Global.asax               # Application entry point
??? Global.asax.cs              # Application initialization
??? Web.config   # Main configuration file
??? Web.Debug.config        # Debug transformation
??? Web.Release.config      # Release transformation
??? packages.config       # NuGet packages
??? MvcFull.csproj             # Project file
```

## Benchmark Endpoints

- `/` - Home page with links to all benchmarks
- `/plaintext` - Plaintext response
- `/json` - JSON serialization
- `/db` - Single database query
- `/queries/{count}` - Multiple database queries (count: 1-500)
- `/updates/{count}` - Database updates (count: 1-500)
- `/fortunes` - Fortunes with server-side rendering

## Configuration

Update the connection string in `Web.config`:

```xml
<connectionStrings>
  <add name="DefaultConnection" 
 connectionString="Server=localhost;Port=5432;Database=hello_world;User Id=benchmarkdbuser;Password=benchmarkdbpass;" 
    providerName="Npgsql" />
</connectionStrings>
```

## Dependencies

- ASP.NET MVC 5.2.9
- Entity Framework 6.4.4
- Npgsql 4.1.14 (PostgreSQL provider)
- Npgsql.EntityFramework 4.1.3.1
- Newtonsoft.Json 13.0.3

## Building and Running

1. Open the solution in Visual Studio
2. Restore NuGet packages
3. Update the connection string in Web.config
4. Build the project (F6)
5. Run with IIS Express (F5) or deploy to IIS

## Notes

- This project uses the traditional ASP.NET MVC 5 framework
- Entity Framework 6 is used for database operations
- The Dapper implementation has been omitted as requested
- Controllers manually create DbContext instances (no DI container)
- Random number generation uses `new Random()` instead of `Random.Shared`
